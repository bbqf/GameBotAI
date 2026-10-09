using System.Net;
using FluentAssertions;
using GameBot.Domain.Versioning;
using GameBot.Service.Services.Updates;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using System.Diagnostics.CodeAnalysis;
using Xunit;

namespace GameBot.UnitTests.Updates;

[SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "Test helper. The HttpClient only wraps a fake handler that holds no resources.")]
[SuppressMessage("Design", "CA1054:URI parameters should not be strings", Justification = "InlineData cannot hold a Uri.")]
public sealed class GitHubReleaseClientTests {
  private const string ReleaseUrl = "https://api.github.com/repos/bbqf/GameBotAI/releases/latest";
  private const string ManifestUrl = "https://github.com/bbqf/GameBotAI/releases/download/v1.7.0.430/update-manifest.json";

  private static GitHubReleaseClient Client(FakeHttpHandler handler, string repository = "bbqf/GameBotAI") =>
    new(
      new HttpClient(handler),
      Options.Create(new UpdateOptions { Repository = repository }),
      NullLogger<GitHubReleaseClient>.Instance);

  private static FakeHttpHandler Server(string releaseJson, string manifestJson) =>
    new(request => request.RequestUri!.ToString() switch {
      ReleaseUrl => FakeHttpHandler.Json(releaseJson),
      ManifestUrl => FakeHttpHandler.Json(manifestJson),
      _ => new HttpResponseMessage(HttpStatusCode.NotFound)
    });

  private static async Task<UpdateFailureException> FailureOf(GitHubReleaseClient client) {
    var act = () => client.GetLatestReleaseAsync(CancellationToken.None);
    return (await act.Should().ThrowAsync<UpdateFailureException>().ConfigureAwait(true)).Which;
  }

  [Fact]
  public async Task LatestReleaseIsParsed() {
    using var handler = Server(UpdateTestData.ReleaseJson(), UpdateTestData.ManifestJson());

    var release = await Client(handler).GetLatestReleaseAsync(CancellationToken.None).ConfigureAwait(true);

    release.Version.Should().Be(new SemanticVersion(1, 7, 0, 430));
    release.Tag.Should().Be("v1.7.0.430");
    release.Notes.Should().Be("Release text");
    release.MsiSizeBytes.Should().Be(48211968);
    release.MsiSha256.Should().Be(UpdateTestData.Sha);
    release.MsiUrl.ToString().Should().EndWith("/GameBot.msi");
    release.PublishedAtUtc.Year.Should().Be(2026);
    handler.Requests.Should().HaveCount(2);
  }

  [Fact]
  public async Task EmptyNotesBecomeNull() {
    using var handler = Server(UpdateTestData.ReleaseJson(body: ""), UpdateTestData.ManifestJson());

    var release = await Client(handler).GetLatestReleaseAsync(CancellationToken.None).ConfigureAwait(true);

    release.Notes.Should().BeNull();
  }

  [Fact]
  public async Task ManifestRedirectToAnAllowedHostIsFollowed() {
    using var handler = new FakeHttpHandler(request => request.RequestUri!.ToString() switch {
      ReleaseUrl => FakeHttpHandler.Json(UpdateTestData.ReleaseJson()),
      ManifestUrl => FakeHttpHandler.Redirect("https://release-assets.githubusercontent.com/abc/update-manifest.json"),
      "https://release-assets.githubusercontent.com/abc/update-manifest.json" => FakeHttpHandler.Json(UpdateTestData.ManifestJson()),
      _ => new HttpResponseMessage(HttpStatusCode.NotFound)
    });

    var release = await Client(handler).GetLatestReleaseAsync(CancellationToken.None).ConfigureAwait(true);

    release.Version.Should().Be(new SemanticVersion(1, 7, 0, 430));
    handler.Requests.Should().HaveCount(3);
  }

  [Fact]
  public async Task RedirectToABadHostIsRefused() {
    using var handler = new FakeHttpHandler(request => request.RequestUri!.ToString() switch {
      ReleaseUrl => FakeHttpHandler.Json(UpdateTestData.ReleaseJson()),
      ManifestUrl => FakeHttpHandler.Redirect("https://evil.example.com/update-manifest.json"),
      _ => new HttpResponseMessage(HttpStatusCode.NotFound)
    });

    var failure = await FailureOf(Client(handler)).ConfigureAwait(true);

    failure.Error.Code.Should().Be("update_manifest_invalid");
    handler.Requests.Should().NotContain(url => url.Host == "evil.example.com");
  }

  [Fact]
  public async Task MsiOnABadHostIsRefused() {
    using var handler = Server(
      UpdateTestData.ReleaseJson(msiUrl: "https://evil.example.com/GameBot.msi"),
      UpdateTestData.ManifestJson());

    var failure = await FailureOf(Client(handler)).ConfigureAwait(true);

    failure.Error.Code.Should().Be("update_manifest_invalid");
  }

  [Fact]
  public async Task ManifestOnABadHostIsRefused() {
    using var handler = Server(
      UpdateTestData.ReleaseJson(manifestUrl: "http://github.com/x/update-manifest.json"),
      UpdateTestData.ManifestJson());

    var failure = await FailureOf(Client(handler)).ConfigureAwait(true);

    failure.Error.Code.Should().Be("update_manifest_invalid");
    handler.Requests.Should().HaveCount(1);
  }

  [Theory]
  [InlineData(HttpStatusCode.NotFound, "update_no_release")]
  [InlineData(HttpStatusCode.Forbidden, "update_rate_limited")]
  [InlineData(HttpStatusCode.TooManyRequests, "update_rate_limited")]
  [InlineData(HttpStatusCode.InternalServerError, "update_network_error")]
  [InlineData(HttpStatusCode.BadGateway, "update_network_error")]
  public async Task HttpStatusMapsToACode(HttpStatusCode status, string code) {
    using var handler = new FakeHttpHandler(_ => new HttpResponseMessage(status));

    var failure = await FailureOf(Client(handler)).ConfigureAwait(true);

    failure.Error.Code.Should().Be(code);
    failure.Error.Hint.Should().NotBeNullOrWhiteSpace();
  }

  [Fact]
  public async Task RateLimitHintSaysTryAgainLater() {
    using var handler = new FakeHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.Forbidden));

    var failure = await FailureOf(Client(handler)).ConfigureAwait(true);

    failure.Error.Hint.Should().Contain("Try again later");
  }

  [Fact]
  public async Task NetworkErrorMapsToACode() {
    using var handler = new FakeHttpHandler(_ => throw new HttpRequestException("no network"));

    var failure = await FailureOf(Client(handler)).ConfigureAwait(true);

    failure.Error.Code.Should().Be("update_network_error");
  }

  [Fact]
  public async Task TimeoutMapsToANetworkError() {
    using var handler = new FakeHttpHandler(_ => throw new TaskCanceledException("timeout"));

    var failure = await FailureOf(Client(handler)).ConfigureAwait(true);

    failure.Error.Code.Should().Be("update_network_error");
  }

  [Fact]
  public async Task UserCancelIsNotAFailure() {
    using var handler = new FakeHttpHandler(_ => throw new TaskCanceledException("cancel"));
    using var cts = new CancellationTokenSource();
    await cts.CancelAsync().ConfigureAwait(true);

    var act = () => Client(handler).GetLatestReleaseAsync(cts.Token);

    await act.Should().ThrowAsync<OperationCanceledException>().ConfigureAwait(true);
  }

  [Theory]
  [InlineData("v1.7.0")]
  [InlineData("nightly")]
  public async Task TagThatDoesNotParseGivesNoRelease(string tag) {
    using var handler = Server(UpdateTestData.ReleaseJson(tag: tag), UpdateTestData.ManifestJson());

    var failure = await FailureOf(Client(handler)).ConfigureAwait(true);

    failure.Error.Code.Should().Be("update_no_release");
  }

  [Fact]
  public async Task MissingManifestAssetIsInvalid() {
    using var handler = Server("{\"tag_name\":\"v1.7.0.430\",\"assets\":[]}", UpdateTestData.ManifestJson());

    var failure = await FailureOf(Client(handler)).ConfigureAwait(true);

    failure.Error.Code.Should().Be("update_manifest_invalid");
  }

  [Fact]
  public async Task ReleaseWithoutAssetsPropertyIsInvalid() {
    using var handler = Server("{\"tag_name\":\"v1.7.0.430\"}", UpdateTestData.ManifestJson());

    var failure = await FailureOf(Client(handler)).ConfigureAwait(true);

    failure.Error.Code.Should().Be("update_manifest_invalid");
  }

  [Fact]
  public async Task ManifestWithNewSchemaIsUnsupported() {
    using var handler = Server(UpdateTestData.ReleaseJson(), UpdateTestData.ManifestJson(schema: 2));

    var failure = await FailureOf(Client(handler)).ConfigureAwait(true);

    failure.Error.Code.Should().Be("update_manifest_unsupported");
  }

  [Fact]
  public async Task ManifestVersionThatDiffersFromTheTagIsInvalid() {
    using var handler = Server(UpdateTestData.ReleaseJson(), UpdateTestData.ManifestJson(version: "1.7.0.431"));

    var failure = await FailureOf(Client(handler)).ConfigureAwait(true);

    failure.Error.Code.Should().Be("update_manifest_invalid");
  }

  [Fact]
  public async Task ManifestFetchFailureIsInvalid() {
    using var handler = new FakeHttpHandler(request => request.RequestUri!.ToString() == ReleaseUrl
      ? FakeHttpHandler.Json(UpdateTestData.ReleaseJson())
      : new HttpResponseMessage(HttpStatusCode.NotFound));

    var failure = await FailureOf(Client(handler)).ConfigureAwait(true);

    failure.Error.Code.Should().Be("update_manifest_invalid");
  }

  [Fact]
  public async Task BrokenReleaseJsonGivesNoRelease() {
    using var handler = new FakeHttpHandler(_ => FakeHttpHandler.Json("{ broken"));

    var failure = await FailureOf(Client(handler)).ConfigureAwait(true);

    failure.Error.Code.Should().Be("update_no_release");
  }

  [Fact]
  public async Task BadRepositorySettingGivesNoRelease() {
    using var handler = new FakeHttpHandler(_ => FakeHttpHandler.Json("{}"));

    var failure = await FailureOf(Client(handler, "not a repository")).ConfigureAwait(true);

    failure.Error.Code.Should().Be("update_no_release");
    handler.Requests.Should().BeEmpty();
  }

  [Fact]
  public async Task OptionsChangeTheRepositoryAndTheApiHost() {
    using var handler = new FakeHttpHandler(request => request.RequestUri!.ToString() switch {
      "http://localhost:5999/repos/me/fork/releases/latest" => FakeHttpHandler.Json("{\"tag_name\":\"v1.0.0.1\",\"assets\":[]}"),
      _ => new HttpResponseMessage(HttpStatusCode.NotFound)
    });
    var client = new GitHubReleaseClient(
      new HttpClient(handler),
      Options.Create(new UpdateOptions { Repository = "me/fork", ApiBaseUrl = new Uri("http://localhost:5999") }),
      NullLogger<GitHubReleaseClient>.Instance);

    var failure = await FailureOf(client).ConfigureAwait(true);

    failure.Error.Code.Should().Be("update_manifest_invalid");
    handler.Requests.Should().ContainSingle().Which.ToString().Should().Be("http://localhost:5999/repos/me/fork/releases/latest");
  }

  [Theory]
  [InlineData("https://github.com/a/b", true)]
  [InlineData("https://objects.githubusercontent.com/x", true)]
  [InlineData("https://release-assets.githubusercontent.com/x", true)]
  [InlineData("http://github.com/a/b", false)]
  [InlineData("https://evilgithub.com/a", false)]
  [InlineData("https://github.com.evil.com/a", false)]
  [InlineData("https://githubusercontent.com/a", false)]
  [InlineData("https://api.github.com/a", false)]
  public void HostListAcceptsOnlyGitHubHosts(string url, bool allowed) {
    UpdateHttp.IsAllowedUrl(new Uri(url)).Should().Be(allowed);
  }

  [Fact]
  public void NullUrlIsNotAllowed() {
    UpdateHttp.IsAllowedUrl(null).Should().BeFalse();
  }

  [Fact]
  public async Task TooManyRedirectsAreRefused() {
    using var handler = new FakeHttpHandler(request => FakeHttpHandler.Redirect(request.RequestUri + "x"));
    using var http = new HttpClient(handler);

    var act = () => UpdateHttp.GetAsync(
      http,
      new Uri("https://github.com/a"),
      HttpCompletionOption.ResponseContentRead,
      new GameBot.Domain.Updates.UpdateError("c", "m", null),
      CancellationToken.None);

    await act.Should().ThrowAsync<UpdateFailureException>().ConfigureAwait(true);
    handler.Requests.Count.Should().BeLessThanOrEqualTo(UpdateHttp.MaxRedirects + 1);
  }

  [Fact]
  public async Task RedirectWithoutALocationIsRefused() {
    using var handler = new FakeHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.Found));
    using var http = new HttpClient(handler);

    var act = () => UpdateHttp.GetAsync(
      http,
      new Uri("https://github.com/a"),
      HttpCompletionOption.ResponseContentRead,
      new GameBot.Domain.Updates.UpdateError("c", "m", null),
      CancellationToken.None);

    await act.Should().ThrowAsync<UpdateFailureException>().ConfigureAwait(true);
  }

  [Fact]
  public async Task RelativeRedirectIsResolvedAgainstTheCurrentUrl() {
    using var handler = new FakeHttpHandler(request => request.RequestUri!.AbsolutePath == "/a"
      ? RelativeRedirect("/b")
      : FakeHttpHandler.Json("{}"));
    using var http = new HttpClient(handler);

    using var response = await UpdateHttp.GetAsync(
      http,
      new Uri("https://github.com/a"),
      HttpCompletionOption.ResponseContentRead,
      new GameBot.Domain.Updates.UpdateError("c", "m", null),
      CancellationToken.None).ConfigureAwait(true);

    response.IsSuccessStatusCode.Should().BeTrue();
    handler.Requests.Last().ToString().Should().Be("https://github.com/b");
  }

  private static HttpResponseMessage RelativeRedirect(string location) {
    var response = new HttpResponseMessage(HttpStatusCode.Found);
    response.Headers.Location = new Uri(location, UriKind.Relative);
    return response;
  }
}
