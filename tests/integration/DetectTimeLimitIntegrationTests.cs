#pragma warning disable CA2007, CA1861, CA1859, CA2000
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using GameBot.Domain.Vision;
using GameBot.Service.Services.Detections;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using OpenCvSharp;
using Xunit;

namespace GameBot.IntegrationTests;

/// <summary>
/// Issue #223: <c>POST /api/images/detect</c> must not report a time-limited measurement as an
/// absence. Each reference gets the full time limit, and an expired limit is a
/// <c>504 detection_timeout</c>, never a 200 with an empty <c>matches</c> array.
/// </summary>
/// <remarks>
/// The tests replace the matcher with one that waits for a fixed time for each reference and then
/// calls the real matcher. Thus the time of each call does not depend on the speed of the machine.
/// </remarks>
[Collection("ConfigIsolation")]
public sealed class DetectTimeLimitIntegrationTests : IDisposable {
  private readonly string? _prevUseAdb;
  private readonly string? _prevToken;
  private readonly string? _prevDynamicPort;
  private readonly string? _prevScreen;
  private readonly string? _prevCaptureInterval;

  public DetectTimeLimitIntegrationTests() {
    _prevUseAdb = Environment.GetEnvironmentVariable("GAMEBOT_USE_ADB");
    _prevToken = Environment.GetEnvironmentVariable("GAMEBOT_AUTH_TOKEN");
    _prevDynamicPort = Environment.GetEnvironmentVariable("GAMEBOT_DYNAMIC_PORT");
    _prevScreen = Environment.GetEnvironmentVariable("GAMEBOT_TEST_SCREEN_IMAGE_B64");
    _prevCaptureInterval = Environment.GetEnvironmentVariable("GAMEBOT_CAPTURE_INTERVAL_MS");

    Environment.SetEnvironmentVariable("GAMEBOT_USE_ADB", "false");
    Environment.SetEnvironmentVariable("GAMEBOT_AUTH_TOKEN", "test-token");
    Environment.SetEnvironmentVariable("GAMEBOT_DYNAMIC_PORT", "true");
    Environment.SetEnvironmentVariable("GAMEBOT_CAPTURE_INTERVAL_MS", "10");
    // The screen shows the "night" art of the alternates fixtures.
    Environment.SetEnvironmentVariable("GAMEBOT_TEST_SCREEN_IMAGE_B64", AlternatesFixtures.NightFrameBase64());
    TestEnvironment.PrepareCleanDataDir();
  }

  public void Dispose() {
    Environment.SetEnvironmentVariable("GAMEBOT_USE_ADB", _prevUseAdb);
    Environment.SetEnvironmentVariable("GAMEBOT_AUTH_TOKEN", _prevToken);
    Environment.SetEnvironmentVariable("GAMEBOT_DYNAMIC_PORT", _prevDynamicPort);
    Environment.SetEnvironmentVariable("GAMEBOT_TEST_SCREEN_IMAGE_B64", _prevScreen);
    Environment.SetEnvironmentVariable("GAMEBOT_CAPTURE_INTERVAL_MS", _prevCaptureInterval);
    GC.SuppressFinalize(this);
  }

  [Fact(DisplayName = "Detect: an expired time limit is a 504 detection_timeout, not an empty 200")]
  public async Task ExpiredTimeLimitIsExplicitFailure() {
    using var baseFactory = new WebApplicationFactory<Program>();
    // The matcher does not complete before the limit. The 30 s wait is only a safety stop.
    using var app = CreateApp(baseFactory, timeoutMs: 100, delayPerReference: TimeSpan.FromSeconds(30));
    using var client = ImageAlternatesIntegrationTests.CreateClient(app);
    await ImageAlternatesIntegrationTests.UploadAsync(client, "anchor", AlternatesFixtures.NightTemplateBase64());

    using var resp = await PostDetectAsync(client, "anchor");

    resp.StatusCode.Should().Be(HttpStatusCode.GatewayTimeout);
    using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
    doc.RootElement.GetProperty("code").GetString().Should().Be("detection_timeout");
    doc.RootElement.GetProperty("message").GetString().Should().NotBeNullOrWhiteSpace();
    doc.RootElement.TryGetProperty("matches", out _).Should().BeFalse("a failure has no match list that can look like an absence");
  }

  [Fact(DisplayName = "Detect: an image with two alternates gets the time limit for each reference")]
  public async Task EachReferenceGetsTheFullTimeLimit() {
    using var baseFactory = new WebApplicationFactory<Program>();
    // Three references at 250 ms each take 750 ms. One shared 500 ms limit expired; 3 x 500 ms does not.
    using var app = CreateApp(baseFactory, timeoutMs: 500, delayPerReference: TimeSpan.FromMilliseconds(250));
    using var client = ImageAlternatesIntegrationTests.CreateClient(app);
    await ImageAlternatesIntegrationTests.UploadAsync(client, "anchor", AlternatesFixtures.DayTemplateBase64());
    await ImageAlternatesIntegrationTests.UploadAsync(client, "anchor-n1", AlternatesFixtures.OtherTemplateBase64());
    await ImageAlternatesIntegrationTests.UploadAsync(client, "anchor-night", AlternatesFixtures.NightTemplateBase64());
    (await ImageAlternatesIntegrationTests.PutAlternatesAsync(client, "anchor", "anchor-n1", "anchor-night"))
      .StatusCode.Should().Be(HttpStatusCode.OK);

    using var resp = await PostDetectAsync(client, "anchor");

    resp.StatusCode.Should().Be(HttpStatusCode.OK);
    using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
    var matches = doc.RootElement.GetProperty("matches");
    matches.GetArrayLength().Should().Be(1, "the night alternate is on the screen");
    matches[0].GetProperty("matchedReferenceId").GetString().Should().Be("anchor-night");
    doc.RootElement.GetProperty("limitsHit").GetBoolean().Should().BeFalse();
  }

  [Fact(DisplayName = "Detect: a deleted alternate does not add to the time limit")]
  public async Task DeletedAlternateDoesNotAddTime() {
    using var baseFactory = new WebApplicationFactory<Program>();
    // Two references load, so the limit is 2 x 250 ms = 500 ms and the work is at least 600 ms.
    // If the deleted alternate counted, the limit is 750 ms and the call gives a 200.
    using var app = CreateApp(baseFactory, timeoutMs: 250, delayPerReference: TimeSpan.FromMilliseconds(300));
    using var client = ImageAlternatesIntegrationTests.CreateClient(app);
    await ImageAlternatesIntegrationTests.UploadAsync(client, "anchor", AlternatesFixtures.DayTemplateBase64());
    await ImageAlternatesIntegrationTests.UploadAsync(client, "anchor-night", AlternatesFixtures.NightTemplateBase64());
    await ImageAlternatesIntegrationTests.UploadAsync(client, "gone", AlternatesFixtures.OtherTemplateBase64());
    (await ImageAlternatesIntegrationTests.PutAlternatesAsync(client, "anchor", "anchor-night", "gone"))
      .StatusCode.Should().Be(HttpStatusCode.OK);
    (await client.DeleteAsync(new Uri("/api/images/gone", UriKind.Relative))).StatusCode.Should().Be(HttpStatusCode.NoContent);

    using var resp = await PostDetectAsync(client, "anchor");

    resp.StatusCode.Should().Be(HttpStatusCode.GatewayTimeout);
    using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
    doc.RootElement.GetProperty("code").GetString().Should().Be("detection_timeout");
  }

  private static WebApplicationFactory<Program> CreateApp(WebApplicationFactory<Program> baseFactory, int timeoutMs, TimeSpan delayPerReference) =>
    baseFactory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services => {
      services.RemoveAll<ITemplateMatcher>();
      services.AddSingleton<ITemplateMatcher>(new DelayedTemplateMatcher(delayPerReference));
      services.PostConfigure<DetectionOptions>(o => o.TimeoutMs = timeoutMs);
    }));

  private static Task<HttpResponseMessage> PostDetectAsync(HttpClient client, string id) =>
    client.PostAsJsonAsync(new Uri("/api/images/detect", UriKind.Relative),
      new { referenceImageId = id, threshold = ImageAlternatesIntegrationTests.Gate, maxResults = 1 });

  /// <summary>Waits for a fixed time for each reference, then scores it with the real matcher.</summary>
  private sealed class DelayedTemplateMatcher : ITemplateMatcher {
    private readonly TemplateMatcher _inner = new();
    private readonly TimeSpan _delay;

    public DelayedTemplateMatcher(TimeSpan delay) => _delay = delay;

    public async Task<TemplateMatchResult> MatchAllAsync(Mat screenshot, Mat templateMat, TemplateMatcherConfig config, CancellationToken cancellationToken = default) {
      await Task.Delay(_delay, cancellationToken);
      return await _inner.MatchAllAsync(screenshot, templateMat, config, cancellationToken);
    }
  }
}
