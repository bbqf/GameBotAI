using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using GameBot.Domain.Sessions;
using GameBot.Emulator.Session;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace GameBot.IntegrationTests;

/// <summary>
/// Feature 106 (FR-010, FR-011): the screenshot endpoint through the full service host. These tests
/// cover the cached-frame staleness headers, the 504 capture_timeout of a hung direct capture, the
/// 503 answer when the direct capture fails, and the device selection by serial.
/// </summary>
public sealed class EmulatorScreenshotCaptureTests {
  private const int CaptureTimeoutMs = 300;

  public EmulatorScreenshotCaptureTests() {
    Environment.SetEnvironmentVariable("GAMEBOT_AUTH_TOKEN", "test-token");
    Environment.SetEnvironmentVariable("GAMEBOT_USE_ADB", "false");
    Environment.SetEnvironmentVariable("GAMEBOT_DYNAMIC_PORT", "true");
    TestEnvironment.PrepareCleanDataDir();
  }

  private static WebApplicationFactory<Program> CreateApp(WebApplicationFactory<Program> baseFactory, ScreenshotFakeSessionManager sessions, bool withCaptureLoop = false) {
    var dataRoot = Environment.GetEnvironmentVariable("GAMEBOT_DATA_DIR")!;
    return baseFactory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services => {
      services.RemoveAll<ISessionManager>();
      services.AddSingleton<ISessionManager>(sessions);
      services.RemoveAll<GameBot.Domain.Images.ImageStorageOptions>();
      services.AddSingleton(new GameBot.Domain.Images.ImageStorageOptions(Path.Combine(dataRoot, "images")));
      services.PostConfigure<DeviceLivenessOptions>(o => {
        o.CaptureTimeoutMs = CaptureTimeoutMs;
        o.StaleLimitMs = 60000;
      });
      if (withCaptureLoop) {
        // A capture loop with a fake device, so that the endpoint serves a cached frame.
        services.RemoveAll<BackgroundScreenCaptureService>();
        services.AddSingleton(sp => new BackgroundScreenCaptureService(
          _ => new FixedFrameProvider(), 50, NullLogger<BackgroundScreenCaptureService>.Instance,
          sp.GetRequiredService<IDeviceLivenessTracker>(), new DeviceLivenessOptions { CaptureTimeoutMs = 2000 }));
      }
    }));
  }

  private static HttpClient Client(WebApplicationFactory<Program> app) {
    var client = app.CreateClient();
    client.DefaultRequestHeaders.Add("Authorization", "Bearer test-token");
    return client;
  }

  private static string Header(HttpResponseMessage resp, string name) => resp.Headers.GetValues(name).Single();

  [Fact]
  public async Task ACachedFrameHasTheStalenessHeaders() {
    var sessions = new ScreenshotFakeSessionManager();
    var session = sessions.Add("emu-int-cache");
    using var baseFactory = new WebApplicationFactory<Program>();
    using var app = CreateApp(baseFactory, sessions, withCaptureLoop: true);
    var client = Client(app);
    var capture = app.Services.GetRequiredService<BackgroundScreenCaptureService>();
    capture.StartCapture(session.Id, "emu-int-cache");
    try {
      // Wait until the loop has a cached frame.
      var sw = Stopwatch.StartNew();
      while (capture.GetCachedFrame(session.Id) is null && sw.ElapsedMilliseconds < 5000) {
        await Task.Delay(25).ConfigureAwait(true);
      }
      capture.GetCachedFrame(session.Id).Should().NotBeNull();

      var resp = await client.GetAsync(new Uri("/api/emulator/screenshot?serial=emu-int-cache", UriKind.Relative)).ConfigureAwait(true);

      resp.StatusCode.Should().Be(HttpStatusCode.OK);
      resp.Content.Headers.ContentType!.MediaType.Should().Be("image/png");
      resp.Headers.Contains("X-Capture-Id").Should().BeTrue();
      long.Parse(Header(resp, "X-Capture-Age-Ms"), CultureInfo.InvariantCulture).Should().BeGreaterThanOrEqualTo(0);
      long.Parse(Header(resp, "X-Capture-Unchanged-Ms"), CultureInfo.InvariantCulture).Should().BeGreaterThanOrEqualTo(0);
      Header(resp, "X-Capture-Stale").Should().Be("false");
    }
    finally {
      capture.StopCapture(session.Id);
    }
  }

  [Fact]
  public async Task ADirectCaptureBySessionIdHasAgeZero() {
    var sessions = new ScreenshotFakeSessionManager();
    var session = sessions.Add("emu-int-direct");
    using var baseFactory = new WebApplicationFactory<Program>();
    using var app = CreateApp(baseFactory, sessions);
    var client = Client(app);

    var resp = await client.GetAsync(new Uri($"/api/emulator/screenshot?sessionId={session.Id}", UriKind.Relative)).ConfigureAwait(true);

    resp.StatusCode.Should().Be(HttpStatusCode.OK);
    Header(resp, "X-Capture-Age-Ms").Should().Be("0");
    Header(resp, "X-Capture-Stale").Should().Be("false");
  }

  [Fact]
  public async Task AHungDirectCaptureGives504() {
    var sessions = new ScreenshotFakeSessionManager { Mode = SnapshotMode.Hang };
    var session = sessions.Add("emu-int-hang");
    using var baseFactory = new WebApplicationFactory<Program>();
    using var app = CreateApp(baseFactory, sessions);
    var client = Client(app);

    var sw = Stopwatch.StartNew();
    var resp = await client.GetAsync(new Uri($"/api/emulator/screenshot?sessionId={session.Id}", UriKind.Relative)).ConfigureAwait(true);

    sw.ElapsedMilliseconds.Should().BeLessThan(CaptureTimeoutMs + 2000);
    resp.StatusCode.Should().Be(HttpStatusCode.GatewayTimeout);
    var body = JsonDocument.Parse(await resp.Content.ReadAsStringAsync().ConfigureAwait(true)).RootElement;
    body.GetProperty("error").GetString().Should().Be("capture_timeout");
    body.GetProperty("message").GetString().Should().Contain($"{CaptureTimeoutMs} ms");
  }

  [Fact]
  public async Task AFailedDirectCaptureGives503() {
    var sessions = new ScreenshotFakeSessionManager { Mode = SnapshotMode.Throw };
    var session = sessions.Add("emu-int-fail");
    using var baseFactory = new WebApplicationFactory<Program>();
    using var app = CreateApp(baseFactory, sessions);
    var client = Client(app);

    var resp = await client.GetAsync(new Uri($"/api/emulator/screenshot?sessionId={session.Id}", UriKind.Relative)).ConfigureAwait(true);

    resp.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
    var body = JsonDocument.Parse(await resp.Content.ReadAsStringAsync().ConfigureAwait(true)).RootElement;
    body.GetProperty("error").GetString().Should().Be("emulator_unavailable");
    body.GetProperty("hint").GetString().Should().NotBeNullOrWhiteSpace();
  }

  [Fact]
  public async Task AnUnknownSerialGives404AndTwoSessionsGive409() {
    var sessions = new ScreenshotFakeSessionManager();
    sessions.Add("emu-int-a");
    sessions.Add("emu-int-b");
    using var baseFactory = new WebApplicationFactory<Program>();
    using var app = CreateApp(baseFactory, sessions);
    var client = Client(app);

    var unknown = await client.GetAsync(new Uri("/api/emulator/screenshot?serial=emu-int-none", UriKind.Relative)).ConfigureAwait(true);
    unknown.StatusCode.Should().Be(HttpStatusCode.NotFound);
    JsonDocument.Parse(await unknown.Content.ReadAsStringAsync().ConfigureAwait(true)).RootElement
      .GetProperty("error").GetString().Should().Be("session_not_found");

    var ambiguous = await client.GetAsync(new Uri("/api/emulator/screenshot", UriKind.Relative)).ConfigureAwait(true);
    ambiguous.StatusCode.Should().Be(HttpStatusCode.Conflict);
    JsonDocument.Parse(await ambiguous.Content.ReadAsStringAsync().ConfigureAwait(true)).RootElement
      .GetProperty("error").GetString().Should().Be("ambiguous_session");
  }

  [Fact]
  public async Task ACropWithAnInvalidNameGives400() {
    var sessions = new ScreenshotFakeSessionManager();
    var session = sessions.Add("emu-int-crop");
    using var baseFactory = new WebApplicationFactory<Program>();
    using var app = CreateApp(baseFactory, sessions);
    var client = Client(app);

    var shot = await client.GetAsync(new Uri($"/api/emulator/screenshot?sessionId={session.Id}", UriKind.Relative)).ConfigureAwait(true);
    shot.StatusCode.Should().Be(HttpStatusCode.OK);
    var captureId = Header(shot, "X-Capture-Id");

    var crop = await client.PostAsJsonAsync(new Uri("/api/images/crop", UriKind.Relative), new {
      name = "bad/name",
      overwrite = true,
      bounds = new { x = 0, y = 0, width = 16, height = 16 },
      sourceCaptureId = captureId
    }).ConfigureAwait(true);

    crop.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    JsonDocument.Parse(await crop.Content.ReadAsStringAsync().ConfigureAwait(true)).RootElement
      .GetProperty("error").GetString().Should().Be("invalid_request");
  }

  private enum SnapshotMode { Ok, Hang, Throw }

  /// <summary>A session manager with sessions that have a device serial but no real device.</summary>
  private sealed class ScreenshotFakeSessionManager : ISessionManager {
    private readonly List<EmulatorSession> _sessions = new();

    public SnapshotMode Mode { get; set; } = SnapshotMode.Ok;

    public int ActiveCount => _sessions.Count;
    public bool CanCreateSession => true;

    public EmulatorSession Add(string serial) {
      var session = new EmulatorSession {
        Id = Guid.NewGuid().ToString("N"),
        GameId = "game-screenshot",
        DeviceSerial = serial,
        Status = SessionStatus.Running,
        Health = SessionHealth.Ok,
        LastActivity = DateTimeOffset.UtcNow
      };
      lock (_sessions) _sessions.Add(session);
      return session;
    }

    public EmulatorSession CreateSession(string gameIdOrPath, string? preferredDeviceSerial = null) => Add(preferredDeviceSerial ?? "emu-int-new");
    public EmulatorSession? GetSession(string id) { lock (_sessions) return _sessions.FirstOrDefault(s => s.Id == id); }
    public IReadOnlyCollection<EmulatorSession> ListSessions() { lock (_sessions) return _sessions.ToList(); }
    public bool StopSession(string id) { lock (_sessions) return _sessions.RemoveAll(s => s.Id == id) > 0; }
    public Task<int> SendInputsAsync(string id, IEnumerable<InputAction> actions, CancellationToken ct = default) => Task.FromResult(0);
    public Task<SessionInputDispatchResult> SendInputsWithResultsAsync(string id, IEnumerable<InputAction> actions, CancellationToken ct = default) =>
      Task.FromResult(new SessionInputDispatchResult(true, Array.Empty<InputActionResult>()));

    public async Task<byte[]> GetSnapshotAsync(string id, CancellationToken ct = default) {
      if (GetSession(id) is null) throw new KeyNotFoundException("Session not found");
      switch (Mode) {
        case SnapshotMode.Hang:
          await Task.Delay(Timeout.Infinite, ct).ConfigureAwait(false);
          break;
        case SnapshotMode.Throw:
          throw new InvalidOperationException("The device did not answer.");
      }
      return FixedFrameProvider.Png;
    }
  }

  /// <summary>A fake device that always returns the same 64x64 PNG.</summary>
  private sealed class FixedFrameProvider : IAdbScreenCaptureProvider {
    public static readonly byte[] Png = CreatePng();

    public Task<byte[]?> CaptureScreenshotPngAsync(CancellationToken ct) => Task.FromResult<byte[]?>(Png);

    private static byte[] CreatePng() {
      using var bmp = new System.Drawing.Bitmap(64, 64);
      using var ms = new MemoryStream();
      bmp.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
      return ms.ToArray();
    }
  }
}
