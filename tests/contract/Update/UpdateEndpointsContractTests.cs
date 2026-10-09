using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using GameBot.Domain.Updates;
using GameBot.Domain.Versioning;
using GameBot.Service.Services.Updates;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace GameBot.ContractTests.Update;

[SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "Test helpers. The factory owns its clients, and the content objects hold no resources.")]
public sealed class UpdateEndpointsContractTests : IDisposable {
  private const string RemoteHeader = "X-Test-Remote-Ip";

  private readonly string? _prevAuthToken;
  private readonly string? _prevUseAdb;
  private readonly string? _prevDynamicPort;
  private readonly string _root = Path.Combine(Path.GetTempPath(), "gamebot-update-contract-" + Guid.NewGuid().ToString("N"));

  private readonly TestReleaseClient _client = new();
  private readonly TestDownloader _downloader = new();
  private readonly TestLauncher _launcher = new();
  private readonly TestInstallGuard _guard = new();
  private readonly TestFreeSpace _space = new();
  private readonly TestLifetime _lifetime = new();
  private UpdateCoordinator? _coordinator;

  public UpdateEndpointsContractTests() {
    _prevAuthToken = Environment.GetEnvironmentVariable("GAMEBOT_AUTH_TOKEN");
    _prevUseAdb = Environment.GetEnvironmentVariable("GAMEBOT_USE_ADB");
    _prevDynamicPort = Environment.GetEnvironmentVariable("GAMEBOT_DYNAMIC_PORT");
    Environment.SetEnvironmentVariable("GAMEBOT_AUTH_TOKEN", "test-token");
    Environment.SetEnvironmentVariable("GAMEBOT_USE_ADB", "false");
    Environment.SetEnvironmentVariable("GAMEBOT_DYNAMIC_PORT", "true");
    Directory.CreateDirectory(_root);
  }

  public void Dispose() {
    Environment.SetEnvironmentVariable("GAMEBOT_AUTH_TOKEN", _prevAuthToken);
    Environment.SetEnvironmentVariable("GAMEBOT_USE_ADB", _prevUseAdb);
    Environment.SetEnvironmentVariable("GAMEBOT_DYNAMIC_PORT", _prevDynamicPort);
    _coordinator?.Dispose();
    _lifetime.Dispose();
    Directory.Delete(_root, recursive: true);
    GC.SuppressFinalize(this);
  }

  private WebApplicationFactory<Program> CreateFactory() =>
    new WebApplicationFactory<Program>().WithWebHostBuilder(builder => builder.ConfigureServices(services => {
      var paths = new UpdatePaths(_root);
      var installed = new TestInstalledVersion();
      var store = new UpdateResultStore(paths, NullLogger<UpdateResultStore>.Instance);
      services.AddSingleton(paths);
      services.AddSingleton<IInstalledVersionProvider>(installed);
      services.AddSingleton<IReleaseClient>(_client);
      services.AddSingleton<IInstallLocationGuard>(_guard);
      services.AddSingleton<IUpdaterLauncher>(_launcher);
      services.AddSingleton<IUpdateDownloader>(_downloader);
      services.AddSingleton<IUpdateResultStore>(store);
      services.AddSingleton<IFreeSpaceProvider>(_space);
      services.AddSingleton<IUpdateQueueStopper>(new TestQueueStopper());
      // The coordinator uses a fake lifetime, so StopApplication does not stop the test host.
      services.AddSingleton(sp => _coordinator = new UpdateCoordinator(
        sp.GetRequiredService<UpdateCheckService>(),
        _downloader,
        _launcher,
        store,
        installed,
        new TestQueueStopper(),
        _space,
        paths,
        _lifetime,
        TimeProvider.System,
        NullLogger<UpdateCoordinator>.Instance));
      // A filter at the start of the pipeline sets the remote address, because TestServer has none.
      services.AddSingleton<IStartupFilter>(new RemoteAddressFilter());
    }));

  private static HttpClient CreateClient(WebApplicationFactory<Program> factory, string? remoteIp = null) {
    var client = factory.CreateClient();
    client.DefaultRequestHeaders.Add("Authorization", "Bearer test-token");
    if (remoteIp is not null) {
      client.DefaultRequestHeaders.Add(RemoteHeader, remoteIp);
    }
    return client;
  }

  private static StringContent Body(string json) => new(json, System.Text.Encoding.UTF8, "application/json");

  private static async Task<JsonElement> ReadJson(HttpResponseMessage response) {
    var text = await response.Content.ReadAsStringAsync().ConfigureAwait(true);
    return JsonDocument.Parse(text).RootElement.Clone();
  }

  private static async Task AssertError(HttpResponseMessage response, HttpStatusCode status, string code) {
    response.StatusCode.Should().Be(status);
    var json = await ReadJson(response).ConfigureAwait(true);
    var error = json.GetProperty("error");
    error.GetProperty("code").GetString().Should().Be(code);
    error.GetProperty("message").GetString().Should().NotBeNullOrWhiteSpace();
    error.TryGetProperty("hint", out _).Should().BeTrue();
  }

  private static async Task CheckForUpdate(HttpClient client) {
    var check = await client.PostAsync(new Uri("/api/update/check", UriKind.Relative), content: null).ConfigureAwait(true);
    check.StatusCode.Should().Be(HttpStatusCode.OK);
  }

  private const string InstallBody = "{\"targetVersion\":\"1.7.0.430\",\"confirmStopQueues\":true}";

  [Fact]
  public async Task StatusReturnsTheInstalledVersionAndInstallPermission() {
    using var factory = CreateFactory();
    var client = CreateClient(factory);

    var response = await client.GetAsync(new Uri("/api/update/status", UriKind.Relative)).ConfigureAwait(true);

    response.StatusCode.Should().Be(HttpStatusCode.OK);
    var json = await ReadJson(response).ConfigureAwait(true);
    json.GetProperty("installedVersion").GetString().Should().Be("1.7.0.412");
    json.GetProperty("canInstallHere").GetBoolean().Should().BeTrue();
    json.GetProperty("installBlockedReason").ValueKind.Should().Be(JsonValueKind.Null);
    json.GetProperty("lastCheck").ValueKind.Should().Be(JsonValueKind.Null);
    json.GetProperty("attempt").ValueKind.Should().Be(JsonValueKind.Null);
    json.GetProperty("lastResult").ValueKind.Should().Be(JsonValueKind.Null);
  }

  [Fact]
  public async Task StatusForARemoteRequestSaysRemote() {
    using var factory = CreateFactory();
    var client = CreateClient(factory, "192.168.1.20");

    var json = await ReadJson(await client.GetAsync(new Uri("/api/update/status", UriKind.Relative)).ConfigureAwait(true)).ConfigureAwait(true);

    json.GetProperty("canInstallHere").GetBoolean().Should().BeFalse();
    json.GetProperty("installBlockedReason").GetString().Should().Be("remote");
  }

  [Fact]
  public async Task StatusForABotThatIsNotInstalledSaysNotInstalled() {
    _guard.Installed = false;
    using var factory = CreateFactory();
    var client = CreateClient(factory);

    var json = await ReadJson(await client.GetAsync(new Uri("/api/update/status", UriKind.Relative)).ConfigureAwait(true)).ConfigureAwait(true);

    json.GetProperty("canInstallHere").GetBoolean().Should().BeFalse();
    json.GetProperty("installBlockedReason").GetString().Should().Be("notInstalled");
  }

  [Fact]
  public async Task RemoteHasPriorityOverNotInstalled() {
    _guard.Installed = false;
    using var factory = CreateFactory();
    var client = CreateClient(factory, "192.168.1.20");

    var json = await ReadJson(await client.GetAsync(new Uri("/api/update/status", UriKind.Relative)).ConfigureAwait(true)).ConfigureAwait(true);

    json.GetProperty("installBlockedReason").GetString().Should().Be("remote");
  }

  [Fact]
  public async Task CheckReturnsUpdateAvailable() {
    using var factory = CreateFactory();
    var client = CreateClient(factory);

    var response = await client.PostAsync(new Uri("/api/update/check", UriKind.Relative), content: null).ConfigureAwait(true);

    response.StatusCode.Should().Be(HttpStatusCode.OK);
    var json = await ReadJson(response).ConfigureAwait(true);
    json.GetProperty("status").GetString().Should().Be("updateAvailable");
    json.GetProperty("installedVersion").GetString().Should().Be("1.7.0.412");
    json.GetProperty("latestVersion").GetString().Should().Be("1.7.0.430");
    json.GetProperty("notes").GetString().Should().Be("Release text");
  }

  [Fact]
  public async Task CheckOnTheNewestVersionReturnsUpToDate() {
    _client.Release = UpdateTestRelease.Make("1.7.0.412");
    using var factory = CreateFactory();
    var client = CreateClient(factory);

    var json = await ReadJson(await client.PostAsync(new Uri("/api/update/check", UriKind.Relative), content: null).ConfigureAwait(true)).ConfigureAwait(true);

    json.GetProperty("status").GetString().Should().Be("upToDate");
  }

  [Fact]
  public async Task FailedCheckReturns200WithCheckFailedAndAnError() {
    _client.Failure = new UpdateFailureException(new UpdateError("update_network_error", "The bot cannot reach GitHub.", "Check the internet connection."));
    using var factory = CreateFactory();
    var client = CreateClient(factory);

    var response = await client.PostAsync(new Uri("/api/update/check", UriKind.Relative), content: null).ConfigureAwait(true);

    response.StatusCode.Should().Be(HttpStatusCode.OK);
    var json = await ReadJson(response).ConfigureAwait(true);
    json.GetProperty("status").GetString().Should().Be("checkFailed");
    json.GetProperty("error").GetProperty("code").GetString().Should().Be("update_network_error");
    json.GetProperty("error").GetProperty("hint").GetString().Should().NotBeNullOrWhiteSpace();
  }

  [Fact]
  public async Task StatusShowsTheLastCheck() {
    using var factory = CreateFactory();
    var client = CreateClient(factory);
    await CheckForUpdate(client).ConfigureAwait(true);

    var json = await ReadJson(await client.GetAsync(new Uri("/api/update/status", UriKind.Relative)).ConfigureAwait(true)).ConfigureAwait(true);

    json.GetProperty("lastCheck").GetProperty("status").GetString().Should().Be("updateAvailable");
  }

  [Fact]
  public async Task InstallStartsTheAttemptAndReturns202() {
    using var factory = CreateFactory();
    var client = CreateClient(factory);
    await CheckForUpdate(client).ConfigureAwait(true);

    var response = await client.PostAsync(new Uri("/api/update/install", UriKind.Relative), Body(InstallBody)).ConfigureAwait(true);

    response.StatusCode.Should().Be(HttpStatusCode.Accepted);
    var json = await ReadJson(response).ConfigureAwait(true);
    json.GetProperty("state").GetString().Should().Be("downloading");
    Guid.TryParse(json.GetProperty("attemptId").GetString(), out _).Should().BeTrue();
    await _coordinator!.Running.ConfigureAwait(true);
    _launcher.Requests.Should().ContainSingle().Which.TargetVersion.Should().Be("1.7.0.430");
    _lifetime.StopCalls.Should().Be(1);
  }

  [Fact]
  public async Task InstallFromARemoteAddressReturns403() {
    using var factory = CreateFactory();
    var client = CreateClient(factory, "192.168.1.20");

    var response = await client.PostAsync(new Uri("/api/update/install", UriKind.Relative), Body(InstallBody)).ConfigureAwait(true);

    await AssertError(response, HttpStatusCode.Forbidden, "update_local_only").ConfigureAwait(true);
    _launcher.Requests.Should().BeEmpty();
  }

  [Fact]
  public async Task InstallFromARemoteAddressOfABotThatIsNotInstalledReturns403() {
    _guard.Installed = false;
    using var factory = CreateFactory();
    var client = CreateClient(factory, "192.168.1.20");

    var response = await client.PostAsync(new Uri("/api/update/install", UriKind.Relative), Body(InstallBody)).ConfigureAwait(true);

    await AssertError(response, HttpStatusCode.Forbidden, "update_local_only").ConfigureAwait(true);
  }

  [Fact]
  public async Task InstallForwardedForHeaderDoesNotMakeARemoteRequestLocal() {
    using var factory = CreateFactory();
    var client = CreateClient(factory, "192.168.1.20");
    client.DefaultRequestHeaders.Add("X-Forwarded-For", "127.0.0.1");

    var response = await client.PostAsync(new Uri("/api/update/install", UriKind.Relative), Body(InstallBody)).ConfigureAwait(true);

    await AssertError(response, HttpStatusCode.Forbidden, "update_local_only").ConfigureAwait(true);
  }

  [Fact]
  public async Task InstallForABotThatIsNotInstalledReturns409() {
    _guard.Installed = false;
    using var factory = CreateFactory();
    var client = CreateClient(factory);

    var response = await client.PostAsync(new Uri("/api/update/install", UriKind.Relative), Body(InstallBody)).ConfigureAwait(true);

    await AssertError(response, HttpStatusCode.Conflict, "update_not_installed").ConfigureAwait(true);
    (await ReadJson(response).ConfigureAwait(true)).GetProperty("error").GetProperty("message").GetString()
      .Should().Be("Update works only for an installed bot");
  }

  [Theory]
  [InlineData("{\"targetVersion\":\"1.7.0.430\"}")]
  [InlineData("{\"targetVersion\":\"1.7.0.430\",\"confirmStopQueues\":false}")]
  [InlineData("not json")]
  public async Task InstallWithoutConfirmationReturns400(string body) {
    using var factory = CreateFactory();
    var client = CreateClient(factory);
    await CheckForUpdate(client).ConfigureAwait(true);

    var response = await client.PostAsync(new Uri("/api/update/install", UriKind.Relative), Body(body)).ConfigureAwait(true);

    await AssertError(response, HttpStatusCode.BadRequest, "update_confirmation_required").ConfigureAwait(true);
    _launcher.Requests.Should().BeEmpty();
  }

  [Fact]
  public async Task InstallWithoutACheckReturns400NotAvailable() {
    using var factory = CreateFactory();
    var client = CreateClient(factory);

    var response = await client.PostAsync(new Uri("/api/update/install", UriKind.Relative), Body(InstallBody)).ConfigureAwait(true);

    await AssertError(response, HttpStatusCode.BadRequest, "update_not_available").ConfigureAwait(true);
  }

  [Fact]
  public async Task InstallWithAnotherVersionReturns400NotAvailable() {
    using var factory = CreateFactory();
    var client = CreateClient(factory);
    await CheckForUpdate(client).ConfigureAwait(true);

    var response = await client.PostAsync(
      new Uri("/api/update/install", UriKind.Relative),
      Body("{\"targetVersion\":\"1.7.0.999\",\"confirmStopQueues\":true}")).ConfigureAwait(true);

    await AssertError(response, HttpStatusCode.BadRequest, "update_not_available").ConfigureAwait(true);
  }

  [Fact]
  public async Task SecondInstallDuringAnInstallReturns409InProgress() {
    var release = new TaskCompletionSource();
    _downloader.Block = release.Task;
    using var factory = CreateFactory();
    var client = CreateClient(factory);
    await CheckForUpdate(client).ConfigureAwait(true);
    (await client.PostAsync(new Uri("/api/update/install", UriKind.Relative), Body(InstallBody)).ConfigureAwait(true))
      .StatusCode.Should().Be(HttpStatusCode.Accepted);

    var second = await client.PostAsync(new Uri("/api/update/install", UriKind.Relative), Body(InstallBody)).ConfigureAwait(true);

    await AssertError(second, HttpStatusCode.Conflict, "update_in_progress").ConfigureAwait(true);
    release.SetResult();
    await _coordinator!.Running.ConfigureAwait(true);
  }

  [Fact]
  public async Task CheckDuringAnInstallReturns409InProgress() {
    var release = new TaskCompletionSource();
    _downloader.Block = release.Task;
    using var factory = CreateFactory();
    var client = CreateClient(factory);
    await CheckForUpdate(client).ConfigureAwait(true);
    await client.PostAsync(new Uri("/api/update/install", UriKind.Relative), Body(InstallBody)).ConfigureAwait(true);

    var check = await client.PostAsync(new Uri("/api/update/check", UriKind.Relative), content: null).ConfigureAwait(true);

    await AssertError(check, HttpStatusCode.Conflict, "update_in_progress").ConfigureAwait(true);
    var status = await ReadJson(await client.GetAsync(new Uri("/api/update/status", UriKind.Relative)).ConfigureAwait(true)).ConfigureAwait(true);
    status.GetProperty("attempt").GetProperty("state").GetString().Should().Be("downloading");
    release.SetResult();
    await _coordinator!.Running.ConfigureAwait(true);
  }

  [Fact]
  public async Task InstallWithLowDiskSpaceReturns422() {
    _space.Free = 10;
    using var factory = CreateFactory();
    var client = CreateClient(factory);
    await CheckForUpdate(client).ConfigureAwait(true);

    var response = await client.PostAsync(new Uri("/api/update/install", UriKind.Relative), Body(InstallBody)).ConfigureAwait(true);

    await AssertError(response, HttpStatusCode.UnprocessableEntity, "update_disk_space").ConfigureAwait(true);
  }

  [Fact]
  public async Task FailedAttemptShowsInTheStatus() {
    _downloader.VerifyFailure = new UpdateFailureException(new UpdateError("update_checksum_mismatch", "The checksum is wrong.", "Try again."));
    using var factory = CreateFactory();
    var client = CreateClient(factory);
    await CheckForUpdate(client).ConfigureAwait(true);
    await client.PostAsync(new Uri("/api/update/install", UriKind.Relative), Body(InstallBody)).ConfigureAwait(true);
    await _coordinator!.Running.ConfigureAwait(true);

    var status = await ReadJson(await client.GetAsync(new Uri("/api/update/status", UriKind.Relative)).ConfigureAwait(true)).ConfigureAwait(true);

    var attempt = status.GetProperty("attempt");
    attempt.GetProperty("state").GetString().Should().Be("failed");
    attempt.GetProperty("errorCode").GetString().Should().Be("update_checksum_mismatch");
    _launcher.Requests.Should().BeEmpty();
  }

  [Fact]
  public async Task UpdateRoutesAreInTheOpenApiDocument() {
    using var factory = CreateFactory();
    var client = CreateClient(factory);

    var response = await client.GetAsync(new Uri("/swagger/v1/swagger.json", UriKind.Relative)).ConfigureAwait(true);

    response.StatusCode.Should().Be(HttpStatusCode.OK);
    var json = await ReadJson(response).ConfigureAwait(true);
    var paths = json.GetProperty("paths");
    paths.TryGetProperty("/api/update/status", out _).Should().BeTrue();
    paths.TryGetProperty("/api/update/check", out _).Should().BeTrue();
    var install = paths.GetProperty("/api/update/install").GetProperty("post");
    install.GetProperty("operationId").GetString().Should().Be("InstallUpdate");
    foreach (var code in new[] { "202", "400", "403", "409", "422" }) {
      install.GetProperty("responses").TryGetProperty(code, out _).Should().BeTrue(code);
    }
  }

  private sealed class RemoteAddressFilter : IStartupFilter {
    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app => {
      app.Use(async (context, nextMiddleware) => {
        var header = context.Request.Headers[RemoteHeader].ToString();
        context.Connection.RemoteIpAddress = System.Net.IPAddress.Parse(string.IsNullOrEmpty(header) ? "127.0.0.1" : header);
        await nextMiddleware(context).ConfigureAwait(false);
      });
      next(app);
    };
  }
}

internal static class UpdateTestRelease {
  public static ReleaseInfo Make(string version) => new(
    SemanticVersion.Parse(version),
    "v" + version,
    new DateTimeOffset(2026, 10, 9, 10, 0, 0, TimeSpan.Zero),
    "Release text",
    new Uri("https://github.com/bbqf/GameBotAI/releases/download/v" + version + "/GameBot.msi"),
    1000,
    new string('a', 64));
}

internal sealed class TestInstalledVersion : IInstalledVersionProvider {
  public SemanticVersion GetInstalledVersion() => new(1, 7, 0, 412);
}

internal sealed class TestReleaseClient : IReleaseClient {
  public ReleaseInfo Release { get; set; } = UpdateTestRelease.Make("1.7.0.430");
  public UpdateFailureException? Failure { get; set; }

  public Task<ReleaseInfo> GetLatestReleaseAsync(CancellationToken ct) =>
    Failure is null ? Task.FromResult(Release) : Task.FromException<ReleaseInfo>(Failure);
}

internal sealed class TestDownloader : IUpdateDownloader {
  public Task? Block { get; set; }
  public UpdateFailureException? VerifyFailure { get; set; }

  public async Task<string> DownloadAsync(ReleaseInfo release, CancellationToken ct) {
    if (Block is not null) {
      await Block.ConfigureAwait(false);
    }

    return "C:\\data\\updates\\GameBot-" + release.Version + ".msi";
  }

  public Task VerifyAsync(string msiPath, ReleaseInfo release, CancellationToken ct) =>
    VerifyFailure is null ? Task.CompletedTask : Task.FromException(VerifyFailure);
}

internal sealed class TestLauncher : IUpdaterLauncher {
  public List<UpdaterLaunchRequest> Requests { get; } = new();

  public void Launch(UpdaterLaunchRequest request) => Requests.Add(request);
}

internal sealed class TestInstallGuard : IInstallLocationGuard {
  public bool Installed { get; set; } = true;

  public bool IsInstalledHere() => Installed;
}

internal sealed class TestFreeSpace : IFreeSpaceProvider {
  public long Free { get; set; } = long.MaxValue;

  public long GetFreeBytes(string path) => Free;
}

internal sealed class TestQueueStopper : IUpdateQueueStopper {
  public Task StopAllAsync(CancellationToken ct) => Task.CompletedTask;
}

internal sealed class TestLifetime : IHostApplicationLifetime, IDisposable {
  private readonly CancellationTokenSource _stopping = new();

  public CancellationToken ApplicationStarted => CancellationToken.None;
  public CancellationToken ApplicationStopping => _stopping.Token;
  public CancellationToken ApplicationStopped => CancellationToken.None;
  public int StopCalls { get; private set; }

  public void Dispose() => _stopping.Dispose();

  public void StopApplication() {
    StopCalls++;
  }
}
