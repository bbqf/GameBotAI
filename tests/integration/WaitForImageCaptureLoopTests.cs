using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Net;
using System.Net.Http.Json;
using System.Runtime.InteropServices;
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
/// Feature 166 (issue #350): a <c>WaitForImage</c> step on a session from <c>POST /api/sessions</c>
/// reads its frame through the real capture loop. Before the fix, that session had no capture loop,
/// thus the step had no frame and timed out when the image was on screen.
/// </summary>
[Collection("ConfigIsolation")]
public sealed class WaitForImageCaptureLoopTests : IDisposable {
  private static readonly string[] AlternateIds = { "wfi-alternate" };
  private readonly WaitForImageCaptureTestHost _host = new();

  public void Dispose() {
    _host.Dispose();
    GC.SuppressFinalize(this);
  }

  [Fact]
  public async Task StepExecuteFindsImageOnScreen() {
    await _host.UploadImageAsync(WaitForImageCaptureTestHost.ImageId, _host.ReferencePngBase64).ConfigureAwait(true);
    _host.Provider.SetFrame(_host.ImageFramePng);
    var sessionId = await _host.CreateSessionAsync().ConfigureAwait(true);
    await _host.WaitForFirstFrameAsync(sessionId).ConfigureAwait(true);

    var (outcome, elapsedMs) = await _host.ExecuteWaitStepAsync(sessionId, WaitForImageCaptureTestHost.ImageId, 3000, 0.85).ConfigureAwait(true);

    outcome.GetProperty("status").GetString().Should().Be("executed");
    outcome.GetProperty("reason").GetString().Should().Be("image_detected");
    outcome.GetProperty("detectionConfidence").ValueKind.Should().Be(JsonValueKind.Number);
    outcome.GetProperty("resolvedPoint").ValueKind.Should().Be(JsonValueKind.Object);
    elapsedMs.Should().BeLessThan(3000);
  }

  [Fact]
  public async Task ForceExecuteFindsImageOnScreen() {
    var client = _host.Client;
    await _host.UploadImageAsync(WaitForImageCaptureTestHost.ImageId, _host.ReferencePngBase64).ConfigureAwait(true);
    _host.Provider.SetFrame(_host.ImageFramePng);
    var sessionId = await _host.CreateSessionAsync().ConfigureAwait(true);
    await _host.WaitForFirstFrameAsync(sessionId).ConfigureAwait(true);
    var commandId = await _host.CreateWaitCommandAsync(WaitForImageCaptureTestHost.ImageId, 3000, 0.85).ConfigureAwait(true);

    var sw = Stopwatch.StartNew();
    var resp = await client.PostAsync(new Uri($"/api/commands/{commandId}/force-execute?sessionId={sessionId}", UriKind.Relative), null).ConfigureAwait(true);
    sw.Stop();

    resp.StatusCode.Should().Be(HttpStatusCode.Accepted);
    using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync().ConfigureAwait(true));
    var outcome = doc.RootElement.GetProperty("stepOutcomes")[0];
    outcome.GetProperty("stepType").GetString().Should().Be("waitForImage");
    outcome.GetProperty("status").GetString().Should().Be("executed");
    outcome.GetProperty("reason").GetString().Should().Be("image_detected");
    sw.ElapsedMilliseconds.Should().BeLessThan(3000);
  }

  [Fact]
  public async Task SequenceInlineWaitFindsImageOnScreen() {
    await _host.UploadImageAsync(WaitForImageCaptureTestHost.ImageId, _host.ReferencePngBase64).ConfigureAwait(true);
    _host.Provider.SetFrame(_host.ImageFramePng);
    var sessionId = await _host.CreateSessionAsync().ConfigureAwait(true);
    await _host.WaitForFirstFrameAsync(sessionId).ConfigureAwait(true);
    var sequenceId = await _host.CreateInlineWaitSequenceAsync(WaitForImageCaptureTestHost.ImageId, 3000, 0.85).ConfigureAwait(true);

    var (step, elapsedMs) = await _host.ExecuteSequenceFirstStepAsync(sequenceId, sessionId).ConfigureAwait(true);

    step.GetProperty("actionOutcome").GetString().Should().Be("image_detected");
    var details = step.GetProperty("waitForImageDetails");
    details.ValueKind.Should().Be(JsonValueKind.Object);
    details.GetProperty("exitCondition").GetString().Should().Be("image_detected");
    elapsedMs.Should().BeLessThan(3000);
  }

  [Fact]
  public async Task SequenceCommandWaitFindsImageOnScreen() {
    var client = _host.Client;
    await _host.UploadImageAsync(WaitForImageCaptureTestHost.ImageId, _host.ReferencePngBase64).ConfigureAwait(true);
    _host.Provider.SetFrame(_host.ImageFramePng);
    var sessionId = await _host.CreateSessionAsync().ConfigureAwait(true);
    await _host.WaitForFirstFrameAsync(sessionId).ConfigureAwait(true);
    var commandId = await _host.CreateWaitCommandAsync(WaitForImageCaptureTestHost.ImageId, 3000, 0.85).ConfigureAwait(true);
    var sequenceId = await _host.CreateCommandStepSequenceAsync(commandId).ConfigureAwait(true);

    var (step, elapsedMs) = await _host.ExecuteSequenceFirstStepAsync(sequenceId, sessionId).ConfigureAwait(true);

    step.GetProperty("actionOutcome").GetString().Should().Be("executed");
    if (step.TryGetProperty("waitForImageDetails", out var details)) {
      details.ValueKind.Should().Be(JsonValueKind.Null);
    }
    elapsedMs.Should().BeLessThan(3000);

    // The nested WaitForImage outcome is in the child execution log entry of the command.
    var logs = _host.Services.GetRequiredService<GameBot.Service.Services.ExecutionLog.IExecutionLogService>();
    var page = await logs.QueryAsync(new GameBot.Domain.Logging.ExecutionLogQuery {
      ObjectType = "command",
      ObjectId = commandId,
      PageSize = 5,
      RootsOnly = false
    }).ConfigureAwait(true);
    page.Items.Should().ContainSingle();
    var nested = page.Items[0].StepOutcomes[0];
    nested.StepType.Should().Be("waitForImage");
    nested.Outcome.Should().Be("executed");
    nested.ReasonText.Should().Be("image_detected");
  }

  [Fact]
  public async Task StepResultAgreesWithDetectEndpoint() {
    await _host.UploadImageAsync(WaitForImageCaptureTestHost.ImageId, _host.ReferencePngBase64).ConfigureAwait(true);
    _host.Provider.SetFrame(_host.ImageFramePng);
    var sessionId = await _host.CreateSessionAsync().ConfigureAwait(true);
    await _host.WaitForFirstFrameAsync(sessionId).ConfigureAwait(true);

    var detectFound = await _host.DetectAsync(sessionId, WaitForImageCaptureTestHost.ImageId, 0.85).ConfigureAwait(true);
    var (found, _) = await _host.ExecuteWaitStepAsync(sessionId, WaitForImageCaptureTestHost.ImageId, 3000, 0.85).ConfigureAwait(true);
    detectFound.Should().BeTrue();
    (found.GetProperty("reason").GetString() == "image_detected").Should().Be(detectFound);

    _host.Provider.SetFrame(_host.NoImageFramePng);
    await _host.WaitForFrameAsync(sessionId, _host.NoImageFramePng).ConfigureAwait(true);
    var detectNotFound = await _host.DetectAsync(sessionId, WaitForImageCaptureTestHost.ImageId, 0.85).ConfigureAwait(true);
    var (notFound, _) = await _host.ExecuteWaitStepAsync(sessionId, WaitForImageCaptureTestHost.ImageId, 1500, 0.85).ConfigureAwait(true);
    detectNotFound.Should().BeFalse();
    (notFound.GetProperty("reason").GetString() == "image_detected").Should().Be(detectNotFound);
  }

  [Fact]
  public async Task StepFindsImageThatComesOnScreenLater() {
    await _host.UploadImageAsync(WaitForImageCaptureTestHost.ImageId, _host.ReferencePngBase64).ConfigureAwait(true);
    _host.Provider.SetFrame(_host.NoImageFramePng);
    var sessionId = await _host.CreateSessionAsync().ConfigureAwait(true);
    await _host.WaitForFirstFrameAsync(sessionId).ConfigureAwait(true);

    var stepTask = _host.ExecuteWaitStepAsync(sessionId, WaitForImageCaptureTestHost.ImageId, 3000, 0.85);
    await Task.Delay(300).ConfigureAwait(true);
    _host.Provider.SetFrame(_host.ImageFramePng);
    var (outcome, elapsedMs) = await stepTask.ConfigureAwait(true);

    outcome.GetProperty("status").GetString().Should().Be("executed");
    outcome.GetProperty("reason").GetString().Should().Be("image_detected");
    elapsedMs.Should().BeLessThan(3000);
  }

  [Fact]
  public async Task StepFindsImageAtLowConfidence() {
    await _host.UploadImageAsync(WaitForImageCaptureTestHost.ImageId, _host.ReferencePngBase64).ConfigureAwait(true);
    _host.Provider.SetFrame(_host.ImageFramePng);
    var sessionId = await _host.CreateSessionAsync().ConfigureAwait(true);
    await _host.WaitForFirstFrameAsync(sessionId).ConfigureAwait(true);

    var (outcome, elapsedMs) = await _host.ExecuteWaitStepAsync(sessionId, WaitForImageCaptureTestHost.ImageId, 3000, 0.5).ConfigureAwait(true);

    outcome.GetProperty("status").GetString().Should().Be("executed");
    outcome.GetProperty("reason").GetString().Should().Be("image_detected");
    elapsedMs.Should().BeLessThan(3000);
  }

  [Fact]
  public async Task StepFindsStoredAlternate() {
    var client = _host.Client;
    // The primary image is a crop of the no-image frame, thus it is not on the image frame.
    await _host.UploadImageAsync("wfi-primary", _host.NotOnScreenPngBase64).ConfigureAwait(true);
    await _host.UploadImageAsync("wfi-alternate", _host.ReferencePngBase64).ConfigureAwait(true);
    var put = await client.PutAsJsonAsync(new Uri("/api/images/wfi-primary/alternates", UriKind.Relative), new { alternates = AlternateIds }).ConfigureAwait(true);
    put.EnsureSuccessStatusCode();
    _host.Provider.SetFrame(_host.ImageFramePng);
    var sessionId = await _host.CreateSessionAsync().ConfigureAwait(true);
    await _host.WaitForFirstFrameAsync(sessionId).ConfigureAwait(true);

    var (outcome, _) = await _host.ExecuteWaitStepAsync(sessionId, "wfi-primary", 3000, 0.85).ConfigureAwait(true);

    outcome.GetProperty("status").GetString().Should().Be("executed");
    outcome.GetProperty("reason").GetString().Should().Be("image_detected");
  }

  public static TheoryData<string> TimeoutPaths => new() { "step", "force", "sequence" };

  [Theory(Skip = "Fails on the baseline of feature 168. Turned off by request. Find the cause and turn it on again.")]
  [MemberData(nameof(TimeoutPaths))]
  public async Task StepTimesOutWhenImageNotOnScreen(string path) {
    const int timeoutMs = 1500;
    var client = _host.Client;
    await _host.UploadImageAsync(WaitForImageCaptureTestHost.ImageId, _host.ReferencePngBase64).ConfigureAwait(true);
    _host.Provider.SetFrame(_host.NoImageFramePng);
    var sessionId = await _host.CreateSessionAsync().ConfigureAwait(true);
    await _host.WaitForFirstFrameAsync(sessionId).ConfigureAwait(true);

    long elapsedMs;
    switch (path) {
      case "step": {
          var (outcome, ms) = await _host.ExecuteWaitStepAsync(sessionId, WaitForImageCaptureTestHost.ImageId, timeoutMs, 0.85).ConfigureAwait(true);
          outcome.GetProperty("status").GetString().Should().Be("completed_timeout");
          outcome.GetProperty("reason").GetString().Should().Be("timeout_elapsed");
          elapsedMs = ms;
          break;
        }
      case "force": {
          var commandId = await _host.CreateWaitCommandAsync(WaitForImageCaptureTestHost.ImageId, timeoutMs, 0.85).ConfigureAwait(true);
          var sw = Stopwatch.StartNew();
          var resp = await client.PostAsync(new Uri($"/api/commands/{commandId}/force-execute?sessionId={sessionId}", UriKind.Relative), null).ConfigureAwait(true);
          sw.Stop();
          using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync().ConfigureAwait(true));
          var outcome = doc.RootElement.GetProperty("stepOutcomes")[0];
          outcome.GetProperty("status").GetString().Should().Be("completed_timeout");
          outcome.GetProperty("reason").GetString().Should().Be("timeout_elapsed");
          elapsedMs = sw.ElapsedMilliseconds;
          break;
        }
      default: {
          var sequenceId = await _host.CreateInlineWaitSequenceAsync(WaitForImageCaptureTestHost.ImageId, timeoutMs, 0.85).ConfigureAwait(true);
          var (step, ms) = await _host.ExecuteSequenceFirstStepAsync(sequenceId, sessionId).ConfigureAwait(true);
          step.GetProperty("actionOutcome").GetString().Should().Be("timeout_elapsed");
          step.GetProperty("waitForImageDetails").GetProperty("exitCondition").GetString().Should().Be("timeout_elapsed");
          elapsedMs = ms;
          break;
        }
    }

    elapsedMs.Should().BeLessThanOrEqualTo(timeoutMs + 500);
    elapsedMs.Should().BeLessThan(2 * timeoutMs);
  }

  [Fact]
  public async Task StepTimesOutInTimeAtLowConfidence() {
    const int timeoutMs = 1500;
    await _host.UploadImageAsync(WaitForImageCaptureTestHost.ImageId, _host.ReferencePngBase64).ConfigureAwait(true);
    _host.Provider.SetFrame(_host.NoImageFramePng);
    var sessionId = await _host.CreateSessionAsync().ConfigureAwait(true);
    await _host.WaitForFirstFrameAsync(sessionId).ConfigureAwait(true);

    var (outcome, elapsedMs) = await _host.ExecuteWaitStepAsync(sessionId, WaitForImageCaptureTestHost.ImageId, timeoutMs, 0.5).ConfigureAwait(true);

    outcome.GetProperty("status").GetString().Should().Be("completed_timeout");
    outcome.GetProperty("reason").GetString().Should().Be("timeout_elapsed");
    elapsedMs.Should().BeLessThanOrEqualTo(timeoutMs + 500);
    elapsedMs.Should().BeLessThan(2 * timeoutMs);
  }

  [Fact]
  public async Task StepTimesOutWhenNoFrameYet() {
    await _host.UploadImageAsync(WaitForImageCaptureTestHost.ImageId, _host.ReferencePngBase64).ConfigureAwait(true);
    _host.Provider.SetFrame(null);
    var sessionId = await _host.CreateSessionAsync().ConfigureAwait(true);

    var (outcome, _) = await _host.ExecuteWaitStepAsync(sessionId, WaitForImageCaptureTestHost.ImageId, 1500, 0.85).ConfigureAwait(true);

    outcome.GetProperty("status").GetString().Should().Be("completed_timeout");
    outcome.GetProperty("reason").GetString().Should().Be("timeout_elapsed");
    outcome.GetProperty("reason").GetString().Should().NotBe("image_detected");
  }

  [Fact]
  public async Task MissingImageStillUnavailable() {
    _host.Provider.SetFrame(_host.ImageFramePng);
    var sessionId = await _host.CreateSessionAsync().ConfigureAwait(true);

    var (outcome, _) = await _host.ExecuteWaitStepAsync(sessionId, "wfi-not-uploaded", 200, 0.85).ConfigureAwait(true);

    outcome.GetProperty("status").GetString().Should().Be("completed_image_unavailable");
    outcome.GetProperty("reason").GetString().Should().Be("image_unavailable");
  }
}

/// <summary>
/// Feature 166: a service host with a fake device. The host keeps <c>GAMEBOT_USE_ADB=false</c>, but it
/// registers a real <see cref="BackgroundScreenCaptureService"/> with a fake capture provider, and the
/// ADB-mode screen sources. Thus a step reads its frame through the real capture loop. The host does
/// not call <c>SessionCaptureCleanup.Attach</c>, because its session manager is a fake.
/// </summary>
internal sealed class WaitForImageCaptureTestHost : IDisposable {
  public const string ImageId = "wfi-anchor";
  public const int FrameWidth = 540;
  public const int FrameHeight = 960;
  public const int CropX = 200;
  public const int CropY = 400;
  public const int CropWidth = 58;
  public const int CropHeight = 22;

  private static readonly string[] EnvNames = {
    "GAMEBOT_USE_ADB", "GAMEBOT_DYNAMIC_PORT", "GAMEBOT_AUTH_TOKEN", "GAMEBOT_DATA_DIR", "GAMEBOT_CAPTURE_INTERVAL_MS", "GAMEBOT_TEST_SCREEN_IMAGE_B64"
  };

  private readonly Dictionary<string, string?> _previousEnv = new(StringComparer.Ordinal);
  private readonly WebApplicationFactory<Program> _baseFactory;
  private readonly WebApplicationFactory<Program> _app;
  private HttpClient? _client;

  public WaitForImageCaptureTestHost() {
    foreach (var name in EnvNames) _previousEnv[name] = Environment.GetEnvironmentVariable(name);
    Environment.SetEnvironmentVariable("GAMEBOT_USE_ADB", "false");
    Environment.SetEnvironmentVariable("GAMEBOT_DYNAMIC_PORT", "true");
    Environment.SetEnvironmentVariable("GAMEBOT_AUTH_TOKEN", "test-token");
    Environment.SetEnvironmentVariable("GAMEBOT_CAPTURE_INTERVAL_MS", "100");
    Environment.SetEnvironmentVariable("GAMEBOT_TEST_SCREEN_IMAGE_B64", null);
    TestEnvironment.PrepareCleanDataDir();

    ImageFramePng = CreateNoiseFramePng(166);
    NoImageFramePng = CreateNoiseFramePng(9166);
    ReferencePngBase64 = Convert.ToBase64String(CropPng(ImageFramePng));
    NotOnScreenPngBase64 = Convert.ToBase64String(CropPng(NoImageFramePng));

    _baseFactory = new WebApplicationFactory<Program>();
    _app = _baseFactory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services => {
      services.RemoveAll<ISessionManager>();
      services.AddSingleton<ISessionManager>(Sessions);
      services.RemoveAll<BackgroundScreenCaptureService>();
      services.AddSingleton(sp => new BackgroundScreenCaptureService(
        _ => Provider, 50, NullLogger<BackgroundScreenCaptureService>.Instance,
        sp.GetRequiredService<IDeviceLivenessTracker>(), new DeviceLivenessOptions { CaptureTimeoutMs = 2000 }));
      services.RemoveAll<GameBot.Domain.Triggers.Evaluators.IScreenSource>();
      services.AddSingleton<GameBot.Domain.Triggers.Evaluators.IScreenSource>(sp => new BackgroundCaptureScreenSource(
        sp.GetRequiredService<BackgroundScreenCaptureService>(), sp.GetRequiredService<ISessionManager>(),
        sp.GetService<IDeviceContextAccessor>()));
      services.RemoveAll<GameBot.Domain.Triggers.Evaluators.IScreenSourceFactory>();
      services.AddSingleton<GameBot.Domain.Triggers.Evaluators.IScreenSourceFactory>(sp =>
        new BackgroundCaptureScreenSourceFactory(sp.GetRequiredService<BackgroundScreenCaptureService>()));
    }));
  }

  /// <summary>The fake session manager. Each new session has a device serial.</summary>
  public CaptureFakeSessionManager Sessions { get; } = new();

  /// <summary>The fake device. A test can change its frame at any time.</summary>
  public ChangeableFrameProvider Provider { get; } = new();

  /// <summary>A 540x960 frame of seeded noise that contains the reference image.</summary>
  public byte[] ImageFramePng { get; }

  /// <summary>A 540x960 frame of seeded noise with a different seed. The reference image is not on it.</summary>
  public byte[] NoImageFramePng { get; }

  /// <summary>The 58x22 reference image: a crop of the image frame, as base64 PNG.</summary>
  public string ReferencePngBase64 { get; }

  /// <summary>A 58x22 crop of the no-image frame, as base64 PNG. It is not on the image frame.</summary>
  public string NotOnScreenPngBase64 { get; }

  /// <summary>The capture service of the host.</summary>
  public BackgroundScreenCaptureService CaptureService => _app.Services.GetRequiredService<BackgroundScreenCaptureService>();

  /// <summary>The service provider of the host.</summary>
  public IServiceProvider Services => _app.Services;

  /// <summary>An HTTP client with the auth token.</summary>
  public HttpClient Client {
    get {
      if (_client is null) {
        _client = _app.CreateClient();
        _client.DefaultRequestHeaders.Add("Authorization", "Bearer test-token");
      }
      return _client;
    }
  }

  public async Task UploadImageAsync(string id, string base64) {
    var resp = await Client.PostAsJsonAsync(new Uri("/api/images", UriKind.Relative), new { id, data = base64 }).ConfigureAwait(false);
    resp.StatusCode.Should().Be(HttpStatusCode.Created);
  }

  public async Task<string> CreateSessionAsync() {
    var resp = await Client.PostAsJsonAsync(new Uri("/api/sessions", UriKind.Relative), new { gameId = "game-166" }).ConfigureAwait(false);
    resp.StatusCode.Should().Be(HttpStatusCode.Created);
    using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync().ConfigureAwait(false));
    return doc.RootElement.GetProperty("id").GetString()!;
  }

  /// <summary>
  /// Waits for the first cached frame of the session, for not more than <paramref name="limitMs"/>.
  /// Returns false when no frame comes. It does not assert, so that a test fails on its own assertion.
  /// </summary>
  public async Task<bool> WaitForFirstFrameAsync(string sessionId, int limitMs = 2000) {
    var sw = Stopwatch.StartNew();
    while (sw.ElapsedMilliseconds < limitMs) {
      if (CaptureService.GetCachedFrame(sessionId) is not null) return true;
      await Task.Delay(20).ConfigureAwait(false);
    }
    return false;
  }

  /// <summary>Waits until the cached frame of the session has the given PNG bytes.</summary>
  public async Task WaitForFrameAsync(string sessionId, byte[] png, int limitMs = 2000) {
    var sw = Stopwatch.StartNew();
    while (sw.ElapsedMilliseconds < limitMs) {
      var frame = CaptureService.GetCachedFrame(sessionId);
      if (frame is not null && frame.PngBytes.AsSpan().SequenceEqual(png)) return;
      await Task.Delay(20).ConfigureAwait(false);
    }
  }

  /// <summary>Sends <c>POST /api/steps/execute</c> with a WaitForImage step. Returns the first outcome and the elapsed time.</summary>
  public async Task<(JsonElement Outcome, long ElapsedMs)> ExecuteWaitStepAsync(string sessionId, string imageId, int timeoutMs, double confidence) {
    var body = new {
      sessionId,
      step = new {
        type = "WaitForImage",
        order = 0,
        waitForImage = new { timeoutMs, detectionTarget = new { referenceImageId = imageId, confidence } }
      }
    };
    var sw = Stopwatch.StartNew();
    var resp = await Client.PostAsJsonAsync(new Uri("/api/steps/execute", UriKind.Relative), body).ConfigureAwait(false);
    sw.Stop();
    resp.StatusCode.Should().Be(HttpStatusCode.Accepted);
    using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync().ConfigureAwait(false));
    return (doc.RootElement.GetProperty("stepOutcomes")[0].Clone(), sw.ElapsedMilliseconds);
  }

  public async Task<string> CreateWaitCommandAsync(string imageId, int timeoutMs, double confidence) {
    var req = new {
      name = "WaitProbe166",
      triggerId = (string?)null,
      steps = new object[] {
        new {
          type = "WaitForImage",
          order = 0,
          waitForImage = new {
            timeoutMs,
            detectionTarget = new { referenceImageId = imageId, confidence, offsetX = 0, offsetY = 0, selectionStrategy = "HighestConfidence" }
          }
        }
      }
    };
    var resp = await Client.PostAsJsonAsync(new Uri("/api/commands", UriKind.Relative), req).ConfigureAwait(false);
    resp.EnsureSuccessStatusCode();
    using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync().ConfigureAwait(false));
    return doc.RootElement.GetProperty("id").GetString()!;
  }

  public Task<string> CreateInlineWaitSequenceAsync(string imageId, int timeoutMs, double confidence) =>
    SaveSequenceAsync(new {
      stepId = "wait-step",
      primitiveAction = new {
        type = "WaitForImage",
        schemaVersion = "v1",
        payload = new { timeoutMs, detectionTarget = new { referenceImageId = imageId, confidence } }
      }
    });

  public Task<string> CreateCommandStepSequenceAsync(string commandId) =>
    SaveSequenceAsync(new {
      stepId = "wait-command",
      primitiveAction = new { type = "command", schemaVersion = "v1", payload = new { commandId } }
    });

  private async Task<string> SaveSequenceAsync(object step) {
    var resp = await Client.PostAsJsonAsync(new Uri("/api/sequences", UriKind.Relative), new {
      name = "wait-capture-sequence",
      version = 1,
      steps = new[] { step }
    }).ConfigureAwait(false);
    resp.StatusCode.Should().Be(HttpStatusCode.Created);
    using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync().ConfigureAwait(false));
    return doc.RootElement.GetProperty("id").GetString()!;
  }

  /// <summary>Sends <c>POST /api/sequences/{id}/execute</c>. Returns the first step result and the elapsed time.</summary>
  public async Task<(JsonElement Step, long ElapsedMs)> ExecuteSequenceFirstStepAsync(string sequenceId, string sessionId) {
    var sw = Stopwatch.StartNew();
    var resp = await Client.PostAsJsonAsync(new Uri($"/api/sequences/{sequenceId}/execute", UriKind.Relative), new { sessionId }).ConfigureAwait(false);
    sw.Stop();
    resp.StatusCode.Should().Be(HttpStatusCode.OK);
    using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync().ConfigureAwait(false));
    return (doc.RootElement.GetProperty("steps")[0].Clone(), sw.ElapsedMilliseconds);
  }

  /// <summary>Sends <c>POST /api/images/detect</c> for the session. Returns true when the call finds the image.</summary>
  public async Task<bool> DetectAsync(string sessionId, string imageId, double threshold) {
    var resp = await Client.PostAsJsonAsync(new Uri("/api/images/detect", UriKind.Relative),
      new { referenceImageId = imageId, threshold, sessionId }).ConfigureAwait(false);
    resp.StatusCode.Should().Be(HttpStatusCode.OK);
    using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync().ConfigureAwait(false));
    return doc.RootElement.GetProperty("matches").GetArrayLength() > 0;
  }

  public void Dispose() {
    _client?.Dispose();
    _app.Dispose();
    _baseFactory.Dispose();
    foreach (var (name, value) in _previousEnv) Environment.SetEnvironmentVariable(name, value);
  }

  /// <summary>Makes a frame of seeded noise. Each pixel has a random color.</summary>
  private static byte[] CreateNoiseFramePng(int seed) {
#pragma warning disable CA5394 // Not a security use: test data only.
    var random = new Random(seed);
    using var bmp = new Bitmap(FrameWidth, FrameHeight, PixelFormat.Format24bppRgb);
    var data = bmp.LockBits(new Rectangle(0, 0, FrameWidth, FrameHeight), ImageLockMode.WriteOnly, PixelFormat.Format24bppRgb);
    try {
      var bytes = new byte[data.Stride * FrameHeight];
      random.NextBytes(bytes);
      Marshal.Copy(bytes, 0, data.Scan0, bytes.Length);
    }
    finally {
      bmp.UnlockBits(data);
    }
#pragma warning restore CA5394
    using var ms = new MemoryStream();
    bmp.Save(ms, ImageFormat.Png);
    return ms.ToArray();
  }

  /// <summary>Crops the reference image at a fixed point of a frame.</summary>
  private static byte[] CropPng(byte[] framePng) {
    using var input = new MemoryStream(framePng, writable: false);
    using var frame = new Bitmap(input);
    using var crop = frame.Clone(new Rectangle(CropX, CropY, CropWidth, CropHeight), PixelFormat.Format24bppRgb);
    using var ms = new MemoryStream();
    crop.Save(ms, ImageFormat.Png);
    return ms.ToArray();
  }
}

/// <summary>A fake device. It returns the current frame, or no frame when the frame is null.</summary>
internal sealed class ChangeableFrameProvider : IAdbScreenCaptureProvider {
  private byte[]? _frame;

  public void SetFrame(byte[]? png) => Volatile.Write(ref _frame, png);

  public Task<byte[]?> CaptureScreenshotPngAsync(CancellationToken ct) => Task.FromResult(Volatile.Read(ref _frame));
}

/// <summary>A session manager whose sessions have a device serial but no real device.</summary>
internal sealed class CaptureFakeSessionManager : ISessionManager {
  private readonly List<EmulatorSession> _sessions = new();

  public int ActiveCount { get { lock (_sessions) return _sessions.Count; } }
  public bool CanCreateSession => true;

  public EmulatorSession CreateSession(string gameIdOrPath, string? preferredDeviceSerial = null) {
    var session = new EmulatorSession {
      Id = Guid.NewGuid().ToString("N"),
      GameId = gameIdOrPath,
      DeviceSerial = preferredDeviceSerial ?? "emulator-test",
      Status = SessionStatus.Running,
      Health = SessionHealth.Ok,
      LastActivity = DateTimeOffset.UtcNow
    };
    lock (_sessions) _sessions.Add(session);
    return session;
  }

  public EmulatorSession? GetSession(string id) { lock (_sessions) return _sessions.FirstOrDefault(s => s.Id == id); }
  public IReadOnlyCollection<EmulatorSession> ListSessions() { lock (_sessions) return _sessions.ToList(); }
  public bool StopSession(string id) { lock (_sessions) return _sessions.RemoveAll(s => s.Id == id) > 0; }
  public Task<int> SendInputsAsync(string id, IEnumerable<InputAction> actions, CancellationToken ct = default) => Task.FromResult(0);
  public Task<SessionInputDispatchResult> SendInputsWithResultsAsync(string id, IEnumerable<InputAction> actions, CancellationToken ct = default) =>
    Task.FromResult(new SessionInputDispatchResult(true, Array.Empty<InputActionResult>()));
  public Task<byte[]> GetSnapshotAsync(string id, CancellationToken ct = default) => Task.FromResult(Array.Empty<byte>());
}
