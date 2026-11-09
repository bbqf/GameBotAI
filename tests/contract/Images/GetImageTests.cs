using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace GameBot.ContractTests.Images;

public sealed class GetImageTests : IDisposable {
  private const string OneByOnePngBase64 = "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO2n5u4AAAAASUVORK5CYII=";

  private readonly string? _prevDynPort;
  private readonly string? _prevToken;
  private readonly string? _prevUseAdb;
  private readonly string? _prevDataDir;
  private readonly string? _prevStorageRoot;
  private readonly string _dataDir;

  public GetImageTests() {
    _prevUseAdb = Environment.GetEnvironmentVariable("GAMEBOT_USE_ADB");
    _prevDynPort = Environment.GetEnvironmentVariable("GAMEBOT_DYNAMIC_PORT");
    _prevToken = Environment.GetEnvironmentVariable("GAMEBOT_AUTH_TOKEN");
    _prevDataDir = Environment.GetEnvironmentVariable("GAMEBOT_DATA_DIR");
    _prevStorageRoot = Environment.GetEnvironmentVariable("Service__Storage__Root");

    _dataDir = Path.Combine(Path.GetTempPath(), $"gamebot-contract-getimg-{Guid.NewGuid():N}");
    Directory.CreateDirectory(_dataDir);

    Environment.SetEnvironmentVariable("GAMEBOT_USE_ADB", "false");
    Environment.SetEnvironmentVariable("GAMEBOT_DYNAMIC_PORT", "true");
    Environment.SetEnvironmentVariable("GAMEBOT_AUTH_TOKEN", "test-token");
    Environment.SetEnvironmentVariable("GAMEBOT_DATA_DIR", _dataDir);
    Environment.SetEnvironmentVariable("Service__Storage__Root", _dataDir);
  }

  public void Dispose() {
    Environment.SetEnvironmentVariable("GAMEBOT_USE_ADB", _prevUseAdb);
    Environment.SetEnvironmentVariable("GAMEBOT_DYNAMIC_PORT", _prevDynPort);
    Environment.SetEnvironmentVariable("GAMEBOT_AUTH_TOKEN", _prevToken);
    Environment.SetEnvironmentVariable("GAMEBOT_DATA_DIR", _prevDataDir);
    Environment.SetEnvironmentVariable("Service__Storage__Root", _prevStorageRoot);

    try {
      if (Directory.Exists(_dataDir)) Directory.Delete(_dataDir, recursive: true);
    }
    catch { }

    GC.SuppressFinalize(this);
  }

  [Fact]
  public async Task GetReturnsSavedImageWithContentType() {
    using var app = new WebApplicationFactory<Program>();
    var client = app.CreateClient();
    client.DefaultRequestHeaders.Add("Authorization", "Bearer test-token");

    var upload = await client.PostAsJsonAsync(new Uri("/api/images", UriKind.Relative), new { id = "get-me", data = OneByOnePngBase64 });
    upload.StatusCode.Should().Be(HttpStatusCode.Created);

    var resp = await client.GetAsync(new Uri("/api/images/get-me", UriKind.Relative));
    resp.StatusCode.Should().Be(HttpStatusCode.OK);
    resp.Content.Headers.ContentType.Should().Be(new MediaTypeHeaderValue("image/png"));
    var bytes = await resp.Content.ReadAsByteArrayAsync();
    bytes.Should().NotBeNull();
    Convert.ToBase64String(bytes).Should().Be(OneByOnePngBase64);
  }

  private static async Task<(HttpClient Client, WebApplicationFactory<Program> App)> StartWithImageAsync(string id) {
    var app = new WebApplicationFactory<Program>();
    var client = app.CreateClient();
    client.DefaultRequestHeaders.Add("Authorization", "Bearer test-token");
    var upload = await client.PostAsJsonAsync(new Uri("/api/images", UriKind.Relative), new { id, data = OneByOnePngBase64 }).ConfigureAwait(false);
    upload.StatusCode.Should().Be(HttpStatusCode.Created);
    return (client, app);
  }

  private static async Task<HttpResponseMessage> GetWithAsync(HttpClient client, string id, Action<HttpRequestMessage> configure) {
    using var req = new HttpRequestMessage(HttpMethod.Get, new Uri($"/api/images/{id}", UriKind.Relative));
    configure(req);
    return await client.SendAsync(req).ConfigureAwait(false);
  }

  [Fact]
  public async Task GetSendsNoCacheAndStrongEtag() {
    var (client, app) = await StartWithImageAsync("etag-me");
    using var _ = app;
    var resp = await client.GetAsync(new Uri("/api/images/etag-me", UriKind.Relative));
    resp.StatusCode.Should().Be(HttpStatusCode.OK);
    resp.Headers.CacheControl.Should().NotBeNull();
    resp.Headers.CacheControl!.NoCache.Should().BeTrue();
    resp.Headers.ETag.Should().NotBeNull();
    resp.Headers.ETag!.IsWeak.Should().BeFalse();
    resp.Headers.ETag.Tag.Should().NotBeNullOrWhiteSpace();
  }

  [Fact]
  public async Task MatchingIfNoneMatchReturns304WithHeaders() {
    var (client, app) = await StartWithImageAsync("etag-304");
    using var _ = app;
    var first = await client.GetAsync(new Uri("/api/images/etag-304", UriKind.Relative));
    var etag = first.Headers.ETag!;

    var second = await GetWithAsync(client, "etag-304", r => r.Headers.IfNoneMatch.Add(etag));
    second.StatusCode.Should().Be(HttpStatusCode.NotModified);
    (await second.Content.ReadAsByteArrayAsync()).Should().BeEmpty();
    second.Headers.CacheControl!.NoCache.Should().BeTrue();
    second.Headers.ETag.Should().Be(etag);
  }

  [Fact]
  public async Task NonMatchingIfNoneMatchReturns200WithBody() {
    var (client, app) = await StartWithImageAsync("etag-other");
    using var _ = app;
    var resp = await GetWithAsync(client, "etag-other", r => r.Headers.IfNoneMatch.Add(new EntityTagHeaderValue("\"other\"")));
    resp.StatusCode.Should().Be(HttpStatusCode.OK);
    (await resp.Content.ReadAsByteArrayAsync()).Should().NotBeEmpty();
  }

  [Fact]
  public async Task IfNoneMatchStarAndListReturn304() {
    var (client, app) = await StartWithImageAsync("etag-star");
    using var _ = app;
    var first = await client.GetAsync(new Uri("/api/images/etag-star", UriKind.Relative));
    var etag = first.Headers.ETag!;

    var star = await GetWithAsync(client, "etag-star", r => r.Headers.IfNoneMatch.Add(EntityTagHeaderValue.Any));
    star.StatusCode.Should().Be(HttpStatusCode.NotModified);

    var list = await GetWithAsync(client, "etag-star", r => {
      r.Headers.IfNoneMatch.Add(new EntityTagHeaderValue("\"wrong\""));
      r.Headers.IfNoneMatch.Add(etag);
    });
    list.StatusCode.Should().Be(HttpStatusCode.NotModified);
  }

  [Fact]
  public async Task IfModifiedSinceStillReturns304() {
    var (client, app) = await StartWithImageAsync("ims-me");
    using var _ = app;
    var first = await client.GetAsync(new Uri("/api/images/ims-me", UriKind.Relative));
    var lastModified = first.Content.Headers.LastModified;
    lastModified.Should().NotBeNull();

    var second = await GetWithAsync(client, "ims-me", r => r.Headers.IfModifiedSince = lastModified);
    second.StatusCode.Should().Be(HttpStatusCode.NotModified);
  }

  [Fact]
  public async Task RangeRequestKeepsPartialContentAndCarriesHeaders() {
    var (client, app) = await StartWithImageAsync("range-me");
    using var _ = app;
    var resp = await GetWithAsync(client, "range-me", r => r.Headers.Range = new RangeHeaderValue(0, 3));
    resp.StatusCode.Should().Be(HttpStatusCode.PartialContent);
    (await resp.Content.ReadAsByteArrayAsync()).Should().HaveCount(4);
    resp.Headers.AcceptRanges.Should().Contain("bytes");
    resp.Headers.ETag.Should().NotBeNull();
    resp.Headers.CacheControl!.NoCache.Should().BeTrue();
  }
}
