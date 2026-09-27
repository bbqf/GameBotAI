#pragma warning disable CA2007 // test code: no ConfigureAwait
using System;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using GameBot.Domain.Sessions;
using GameBot.Emulator.Session;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace GameBot.ContractTests.Sessions;

/// <summary>
/// Feature 106 (FR-010, FR-011, SC-004, contract <c>screenshot-snapshot.md</c>): the staleness headers
/// of the screenshot and snapshot responses, and the 504 <c>capture_timeout</c> of a hung direct capture.
/// </summary>
public sealed class CaptureHeadersContractTests : IDisposable {
  private const int CaptureTimeoutMs = 300;

  private readonly string? _prevAuthToken;
  private readonly string? _prevUseAdb;
  private readonly string? _prevDynamicPort;

  public CaptureHeadersContractTests() {
    _prevAuthToken = Environment.GetEnvironmentVariable("GAMEBOT_AUTH_TOKEN");
    _prevUseAdb = Environment.GetEnvironmentVariable("GAMEBOT_USE_ADB");
    _prevDynamicPort = Environment.GetEnvironmentVariable("GAMEBOT_DYNAMIC_PORT");
    Environment.SetEnvironmentVariable("GAMEBOT_AUTH_TOKEN", "test-token");
    Environment.SetEnvironmentVariable("GAMEBOT_USE_ADB", "false");
    Environment.SetEnvironmentVariable("GAMEBOT_DYNAMIC_PORT", "true");
  }

  public void Dispose() {
    Environment.SetEnvironmentVariable("GAMEBOT_AUTH_TOKEN", _prevAuthToken);
    Environment.SetEnvironmentVariable("GAMEBOT_USE_ADB", _prevUseAdb);
    Environment.SetEnvironmentVariable("GAMEBOT_DYNAMIC_PORT", _prevDynamicPort);
    GC.SuppressFinalize(this);
  }

  private static HttpClient Client(WebApplicationFactory<Program> app) {
    var client = app.CreateClient();
    client.DefaultRequestHeaders.Add("Authorization", "Bearer test-token");
    return client;
  }

  private static WebApplicationFactory<Program> WithSessions(WebApplicationFactory<Program> baseFactory, LivenessFakeSessionManager sessions, Action<IServiceCollection>? more = null) =>
    baseFactory.WithWebHostBuilder(b => b.ConfigureTestServices(services => {
      services.RemoveAll<ISessionManager>();
      services.AddSingleton<ISessionManager>(sessions);
      services.PostConfigure<DeviceLivenessOptions>(o => {
        o.CaptureTimeoutMs = CaptureTimeoutMs;
        o.StaleLimitMs = 1000;
      });
      more?.Invoke(services);
    }));

  private static string Header(HttpResponseMessage resp, string name) => resp.Headers.GetValues(name).Single();

  [Fact]
  public async Task ADirectCaptureHasAllHeadersWithAgeZero() {
    var sessions = new LivenessFakeSessionManager();
    var session = sessions.Add("emu-hdr-1");
    using var baseFactory = new WebApplicationFactory<Program>();
    using var app = WithSessions(baseFactory, sessions);
    var client = Client(app);

    var resp = await client.GetAsync(new Uri($"/api/emulator/screenshot?sessionId={session.Id}", UriKind.Relative));

    resp.StatusCode.Should().Be(HttpStatusCode.OK);
    resp.Headers.Contains("X-Capture-Id").Should().BeTrue();
    Header(resp, "X-Capture-Age-Ms").Should().Be("0");
    Header(resp, "X-Capture-Unchanged-Ms").Should().Be("0");
    Header(resp, "X-Capture-Stale").Should().Be("false");
  }

  [Fact]
  public async Task ACachedFrameThatDidNotChangeIsStale() {
    var sessions = new LivenessFakeSessionManager();
    var session = sessions.Add("emu-hdr-2");
    using var baseFactory = new WebApplicationFactory<Program>();
    using var app = WithSessions(baseFactory, sessions, services => {
      services.RemoveAll<BackgroundScreenCaptureService>();
      services.AddSingleton(sp => new BackgroundScreenCaptureService(
        _ => new SameFrameProvider(), 50, NullLogger<BackgroundScreenCaptureService>.Instance,
        sp.GetRequiredService<IDeviceLivenessTracker>(), new DeviceLivenessOptions { CaptureTimeoutMs = 2000 }));
    });
    var client = Client(app);
    var capture = app.Services.GetRequiredService<BackgroundScreenCaptureService>();
    capture.StartCapture(session.Id, "emu-hdr-2");
    try {
      // The same bytes for longer than StaleLimitMs (1000 ms).
      await Task.Delay(1600);

      var resp = await client.GetAsync(new Uri($"/api/emulator/screenshot?sessionId={session.Id}", UriKind.Relative));

      resp.StatusCode.Should().Be(HttpStatusCode.OK);
      Header(resp, "X-Capture-Stale").Should().Be("true");
      long.Parse(Header(resp, "X-Capture-Unchanged-Ms"), System.Globalization.CultureInfo.InvariantCulture).Should().BeGreaterThan(1000);
      long.Parse(Header(resp, "X-Capture-Age-Ms"), System.Globalization.CultureInfo.InvariantCulture).Should().BeLessThan(1000);
    }
    finally {
      capture.StopCapture(session.Id);
    }
  }

  [Fact]
  public async Task AHungDirectScreenshotGives504InBoundedTime() {
    var sessions = new LivenessFakeSessionManager { HangSnapshots = true };
    var session = sessions.Add("emu-hdr-3");
    using var baseFactory = new WebApplicationFactory<Program>();
    using var app = WithSessions(baseFactory, sessions);
    var client = Client(app);

    var sw = Stopwatch.StartNew();
    var resp = await client.GetAsync(new Uri($"/api/emulator/screenshot?sessionId={session.Id}", UriKind.Relative));

    sw.ElapsedMilliseconds.Should().BeLessThan(CaptureTimeoutMs + 2000);
    resp.StatusCode.Should().Be(HttpStatusCode.GatewayTimeout);
    var body = JsonDocument.Parse(await resp.Content.ReadAsStringAsync()).RootElement;
    body.GetProperty("error").GetString().Should().Be("capture_timeout");
    body.GetProperty("message").GetString().Should().Contain($"{CaptureTimeoutMs} ms");
  }

  [Fact]
  public async Task AHungSnapshotGives504InBoundedTime() {
    var sessions = new LivenessFakeSessionManager { HangSnapshots = true };
    var session = sessions.Add("emu-hdr-4");
    using var baseFactory = new WebApplicationFactory<Program>();
    using var app = WithSessions(baseFactory, sessions);
    var client = Client(app);

    var sw = Stopwatch.StartNew();
    var resp = await client.GetAsync(new Uri($"/api/sessions/{session.Id}/snapshot", UriKind.Relative));

    sw.ElapsedMilliseconds.Should().BeLessThan(CaptureTimeoutMs + 2000);
    resp.StatusCode.Should().Be(HttpStatusCode.GatewayTimeout);
    var error = JsonDocument.Parse(await resp.Content.ReadAsStringAsync()).RootElement.GetProperty("error");
    error.GetProperty("code").GetString().Should().Be("capture_timeout");
    error.GetProperty("message").GetString().Should().NotBeNullOrWhiteSpace();
    error.GetProperty("hint").GetString().Should().NotBeNullOrWhiteSpace();
  }

  [Fact]
  public async Task ASnapshotWithNoCaptureDataHasNoStalenessHeaders() {
    var sessions = new LivenessFakeSessionManager();
    var session = sessions.Add("emu-hdr-5");
    using var baseFactory = new WebApplicationFactory<Program>();
    using var app = WithSessions(baseFactory, sessions);
    var client = Client(app);

    var resp = await client.GetAsync(new Uri($"/api/sessions/{session.Id}/snapshot", UriKind.Relative));

    resp.StatusCode.Should().Be(HttpStatusCode.OK);
    resp.Headers.Contains("X-Capture-Age-Ms").Should().BeFalse();
    resp.Headers.Contains("X-Capture-Id").Should().BeFalse("X-Capture-Id stays on the screenshot endpoint only");
  }

  [Fact]
  public async Task ASnapshotWithCaptureDataHasTheHeaders() {
    var sessions = new LivenessFakeSessionManager();
    var session = sessions.Add("emu-hdr-6");
    using var baseFactory = new WebApplicationFactory<Program>();
    using var app = WithSessions(baseFactory, sessions);
    var client = Client(app);
    var tracker = app.Services.GetRequiredService<IDeviceLivenessTracker>();
    tracker.LoopStarted(session.Id);
    tracker.RecordCapture(session.Id, changed: true);

    var resp = await client.GetAsync(new Uri($"/api/sessions/{session.Id}/snapshot", UriKind.Relative));

    resp.StatusCode.Should().Be(HttpStatusCode.OK);
    Header(resp, "X-Capture-Age-Ms").Should().Be("0");
    Header(resp, "X-Capture-Stale").Should().Be("false");
    resp.Headers.Contains("X-Capture-Unchanged-Ms").Should().BeTrue();
  }

  [Fact]
  public async Task CorsExposesTheFourHeaders() {
    var sessions = new LivenessFakeSessionManager();
    var session = sessions.Add("emu-hdr-7");
    using var baseFactory = new WebApplicationFactory<Program>();
    using var app = WithSessions(baseFactory, sessions);
    var client = Client(app);
    using var request = new HttpRequestMessage(HttpMethod.Get, new Uri($"/api/emulator/screenshot?sessionId={session.Id}", UriKind.Relative));
    request.Headers.Add("Origin", "http://localhost:5173");

    var resp = await client.SendAsync(request);

    resp.StatusCode.Should().Be(HttpStatusCode.OK);
    var exposed = string.Join(",", resp.Headers.GetValues("Access-Control-Expose-Headers"));
    foreach (var name in new[] { "X-Capture-Id", "X-Capture-Age-Ms", "X-Capture-Unchanged-Ms", "X-Capture-Stale" }) {
      exposed.Should().Contain(name);
    }
  }

  [Fact]
  public async Task TheCurrentErrorsDoNotChange() {
    var sessions = new LivenessFakeSessionManager();
    using var baseFactory = new WebApplicationFactory<Program>();
    using var app = WithSessions(baseFactory, sessions);
    var client = Client(app);

    var noSession = await client.GetAsync(new Uri("/api/emulator/screenshot", UriKind.Relative));
    noSession.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
    JsonDocument.Parse(await noSession.Content.ReadAsStringAsync()).RootElement.GetProperty("error").GetString().Should().Be("emulator_unavailable");

    var unknownSerial = await client.GetAsync(new Uri("/api/emulator/screenshot?serial=emu-none", UriKind.Relative));
    unknownSerial.StatusCode.Should().Be(HttpStatusCode.NotFound);

    sessions.Add("emu-a");
    sessions.Add("emu-b");
    var ambiguous = await client.GetAsync(new Uri("/api/emulator/screenshot", UriKind.Relative));
    ambiguous.StatusCode.Should().Be(HttpStatusCode.Conflict);

    var snapshot404 = await client.GetAsync(new Uri("/api/sessions/nope/snapshot", UriKind.Relative));
    snapshot404.StatusCode.Should().Be(HttpStatusCode.NotFound);
  }

  private sealed class SameFrameProvider : IAdbScreenCaptureProvider {
    private static readonly byte[] Png = CreatePng();

    public Task<byte[]?> CaptureScreenshotPngAsync(CancellationToken ct) => Task.FromResult<byte[]?>(Png);

    private static byte[] CreatePng() {
      using var bmp = new System.Drawing.Bitmap(2, 2);
      using var ms = new System.IO.MemoryStream();
      bmp.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
      return ms.ToArray();
    }
  }
}
