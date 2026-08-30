using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace GameBot.ContractTests;

/// <summary>
/// Feature 079, contracts/api.md §3: the emulator screenshot endpoint resolves its device explicitly.
///
/// Before this, an unqualified request with several sessions open returned an arbitrary emulator, so an
/// operator could crop a reference image from the wrong device without noticing. That case is now an
/// explicit ambiguity error; the single-session behaviour is unchanged.
/// </summary>
public sealed class EmulatorScreenshotSelectorContractTests : IDisposable {
  private readonly string? _prevAuthToken;
  private readonly string? _prevUseAdb;
  private readonly string? _prevDynamicPort;
  private readonly string? _prevDataDir;
  private readonly string _dataDir;

  public EmulatorScreenshotSelectorContractTests() {
    _prevAuthToken = Environment.GetEnvironmentVariable("GAMEBOT_AUTH_TOKEN");
    _prevUseAdb = Environment.GetEnvironmentVariable("GAMEBOT_USE_ADB");
    _prevDynamicPort = Environment.GetEnvironmentVariable("GAMEBOT_DYNAMIC_PORT");

    Environment.SetEnvironmentVariable("GAMEBOT_AUTH_TOKEN", "test-token");
    Environment.SetEnvironmentVariable("GAMEBOT_USE_ADB", "false");
    Environment.SetEnvironmentVariable("GAMEBOT_DYNAMIC_PORT", "true");

    // Isolate persistence: these tests create sessions and capture entries, and must not read or
    // write the developer's real data directory.
    _prevDataDir = Environment.GetEnvironmentVariable("GAMEBOT_DATA_DIR");
    _dataDir = Path.Combine(Path.GetTempPath(), "GameBotContractTests", Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(_dataDir);
    Environment.SetEnvironmentVariable("GAMEBOT_DATA_DIR", _dataDir);
    Environment.SetEnvironmentVariable("Service__Storage__Root", _dataDir);
  }

  public void Dispose() {
    Environment.SetEnvironmentVariable("GAMEBOT_AUTH_TOKEN", _prevAuthToken);
    Environment.SetEnvironmentVariable("GAMEBOT_USE_ADB", _prevUseAdb);
    Environment.SetEnvironmentVariable("GAMEBOT_DYNAMIC_PORT", _prevDynamicPort);
    Environment.SetEnvironmentVariable("GAMEBOT_DATA_DIR", _prevDataDir);
    Environment.SetEnvironmentVariable("Service__Storage__Root", null);
    try { Directory.Delete(_dataDir, recursive: true); }
    catch (IOException) { /* best effort */ }
    catch (UnauthorizedAccessException) { /* best effort */ }
    GC.SuppressFinalize(this);
  }

  private static HttpClient NewClient(WebApplicationFactory<Program> app) {
    var client = app.CreateClient();
    client.DefaultRequestHeaders.Add("Authorization", "Bearer test-token");
    return client;
  }

  /// <summary>Creates a session bound to a device serial, returning its id.</summary>
  private static async Task<string> CreateSessionAsync(HttpClient client, string gameId, string serial) {
    var resp = await client.PostAsJsonAsync("/api/sessions", new { gameId, adbSerial = serial }).ConfigureAwait(true);
    resp.StatusCode.Should().Be(HttpStatusCode.Created);
    return JsonDocument.Parse(await resp.Content.ReadAsStringAsync().ConfigureAwait(true)).RootElement.GetProperty("id").GetString()!;
  }

  private static Task<HttpResponseMessage> ScreenshotAsync(HttpClient client, string query = "") =>
    client.GetAsync(new Uri($"/api/emulator/screenshot{query}", UriKind.Relative));

  [Fact] // §3 row 4: no sessions at all is still 503, unchanged
  public async Task WithNoSessionsTheEndpointReports503() {
    using var app = new WebApplicationFactory<Program>();
    var client = NewClient(app);

    var resp = await ScreenshotAsync(client).ConfigureAwait(true);

    resp.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
    JsonDocument.Parse(await resp.Content.ReadAsStringAsync().ConfigureAwait(true))
      .RootElement.GetProperty("error").GetString().Should().Be("emulator_unavailable");
  }

  [Fact] // §3 row 5: exactly one session — the bare call keeps working
  public async Task WithASingleSessionTheBareCallStillSucceeds() {
    using var app = new WebApplicationFactory<Program>();
    var client = NewClient(app);
    await CreateSessionAsync(client, "game-a", "emu-shot-a").ConfigureAwait(true);

    var resp = await ScreenshotAsync(client).ConfigureAwait(true);

    resp.StatusCode.Should().Be(HttpStatusCode.OK);
    resp.Headers.Contains("X-Capture-Id").Should().BeTrue();
  }

  [Fact] // §3 row 6: several sessions and no selector is now an explicit ambiguity error
  public async Task WithSeveralSessionsAndNoSelectorTheEndpointReports409() {
    using var app = new WebApplicationFactory<Program>();
    var client = NewClient(app);
    await CreateSessionAsync(client, "game-a", "emu-shot-a").ConfigureAwait(true);
    await CreateSessionAsync(client, "game-b", "emu-shot-b").ConfigureAwait(true);

    var resp = await ScreenshotAsync(client).ConfigureAwait(true);

    resp.StatusCode.Should().Be(HttpStatusCode.Conflict);
    var body = JsonDocument.Parse(await resp.Content.ReadAsStringAsync().ConfigureAwait(true)).RootElement;
    body.GetProperty("error").GetString().Should().Be("ambiguous_session");
    body.GetProperty("message").GetString().Should().Contain("2 device sessions are active");
  }

  [Fact] // §3 row 1: an explicit sessionId resolves even with several sessions open
  public async Task AnExplicitSessionIdResolvesTheRequestedDevice() {
    using var app = new WebApplicationFactory<Program>();
    var client = NewClient(app);
    var a = await CreateSessionAsync(client, "game-a", "emu-shot-a").ConfigureAwait(true);
    await CreateSessionAsync(client, "game-b", "emu-shot-b").ConfigureAwait(true);

    var resp = await ScreenshotAsync(client, $"?sessionId={a}").ConfigureAwait(true);

    resp.StatusCode.Should().Be(HttpStatusCode.OK);
    resp.Headers.Contains("X-Capture-Id").Should().BeTrue();
  }

  // §3 row 2 (a `serial` that matches a live device returns that device's screen) is not exercised
  // here: ADB is stubbed in the contract suite, so sessions carry no bound serial and no `serial`
  // value can match. The negative half of that row — an unmatched serial must 404 rather than fall
  // back to another device — is what actually guards against the old arbitrary pick, and is covered.
  [Fact] // §3 row 3: an unmatched serial is a 404, never a silent substitution
  public async Task AnUnknownSerialReports404() {
    using var app = new WebApplicationFactory<Program>();
    var client = NewClient(app);
    await CreateSessionAsync(client, "game-a", "emu-shot-a").ConfigureAwait(true);

    var resp = await ScreenshotAsync(client, "?serial=emu-does-not-exist").ConfigureAwait(true);

    resp.StatusCode.Should().Be(HttpStatusCode.NotFound);
    JsonDocument.Parse(await resp.Content.ReadAsStringAsync().ConfigureAwait(true))
      .RootElement.GetProperty("error").GetString().Should().Be("session_not_found");
  }
}
