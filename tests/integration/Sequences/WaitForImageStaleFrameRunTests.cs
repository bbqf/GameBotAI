using System.Text.Json;
using FluentAssertions;
using GameBot.Domain.Sessions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace GameBot.IntegrationTests.Sequences;

/// <summary>
/// Feature 197 (US1, US2): a sequence run with a <c>WaitForImage</c> step through the real capture loop and
/// the service host. The fake device stops to give new frames (each capture fails), so the frame in memory
/// gets old. The step must not end with <c>image_detected</c> on that stale frame.
/// </summary>
[Collection("ConfigIsolation")]
public sealed class WaitForImageStaleFrameRunTests : IDisposable {
  private const int FrameMaxAgeMs = 1000;
  private readonly WaitForImageCaptureTestHost _host = new(services =>
    services.PostConfigure<DeviceLivenessOptions>(o => o.FrameMaxAgeMs = FrameMaxAgeMs));

  public void Dispose() {
    _host.Dispose();
    GC.SuppressFinalize(this);
  }

  /// <summary>Waits until the frame in memory is older than the effective limit.</summary>
  private async Task WaitUntilStaleAsync(string sessionId) {
    var intervalMs = _host.Services.GetRequiredService<GameBot.Domain.Config.AppConfig>().CaptureIntervalMs;
    var limitMs = FrameFreshness.EffectiveLimitMs(FrameMaxAgeMs, intervalMs);
    var sw = System.Diagnostics.Stopwatch.StartNew();
    while (sw.ElapsedMilliseconds < 10000) {
      var frame = _host.CaptureService.GetCachedFrame(sessionId);
      if (frame is not null && FrameFreshness.IsStale(frame, DateTimeOffset.UtcNow, limitMs)) return;
      await Task.Delay(50).ConfigureAwait(false);
    }
  }

  [Fact]
  public async Task AStaleFrameThatShowsTheImageGivesTimeoutAndNotImageDetected() {
    // Before the fix, the step ended with image_detected on the old frame.
    await _host.UploadImageAsync(WaitForImageCaptureTestHost.ImageId, _host.ReferencePngBase64).ConfigureAwait(true);
    _host.Provider.SetFrame(_host.ImageFramePng);
    var sessionId = await _host.CreateSessionAsync().ConfigureAwait(true);
    (await _host.WaitForFirstFrameAsync(sessionId).ConfigureAwait(true)).Should().BeTrue();
    _host.Provider.SetFrame(null);
    await WaitUntilStaleAsync(sessionId).ConfigureAwait(true);
    var sequenceId = await _host.CreateInlineWaitSequenceAsync(WaitForImageCaptureTestHost.ImageId, 1500, 0.85).ConfigureAwait(true);

    var (step, _) = await _host.ExecuteSequenceFirstStepAsync(sequenceId, sessionId).ConfigureAwait(true);

    step.GetProperty("actionOutcome").GetString().Should().Be("timeout_elapsed");
    var details = step.GetProperty("waitForImageDetails");
    details.GetProperty("exitCondition").GetString().Should().Be("timeout_elapsed");
    details.GetProperty("staleReason").GetString().Should().Be("stale_frame");
    details.GetProperty("stalePassCount").GetInt32().Should().BeGreaterThan(0);
    details.GetProperty("captureId").GetString().Should().Be(_host.CaptureService.GetCachedFrame(sessionId)!.FrameId);

    // US2: the execution log of the run has the new keys.
    var logs = _host.Services.GetRequiredService<GameBot.Service.Services.ExecutionLog.IExecutionLogService>();
    var page = await logs.QueryAsync(new GameBot.Domain.Logging.ExecutionLogQuery {
      ObjectType = "sequence",
      ObjectId = sequenceId,
      PageSize = 5,
      RootsOnly = false
    }).ConfigureAwait(true);
    var entry = page.Items.Should().ContainSingle().Subject;
    var item = entry.Details.Single(d => d.Attributes is not null
      && d.Attributes.TryGetValue("stepType", out var type) && Equals(type?.ToString(), "waitForImage"));
    var attributes = item.Attributes!;
    foreach (var key in new[] { "captureId", "frameAgeMs", "passCount", "stalePassCount", "staleReason" }) {
      attributes.Should().ContainKey(key);
      attributes[key].Should().NotBeNull(key + " has a value");
    }
    attributes.Should().ContainKey("bestScore");
    var outcome = entry.StepOutcomes.Single(o => o.StepType == "waitForImage");
    outcome.DetailAttributes!.StaleReason.Should().Be("stale_frame");
    outcome.DetailAttributes.CaptureId.Should().NotBeNullOrWhiteSpace();
    outcome.DetailAttributes.StalePassCount.Should().BeGreaterThan(0);
  }

  [Fact]
  public async Task AFreshFrameWithTheImageAfterAStallGivesImageDetected() {
    await _host.UploadImageAsync(WaitForImageCaptureTestHost.ImageId, _host.ReferencePngBase64).ConfigureAwait(true);
    _host.Provider.SetFrame(_host.NoImageFramePng);
    var sessionId = await _host.CreateSessionAsync().ConfigureAwait(true);
    (await _host.WaitForFirstFrameAsync(sessionId).ConfigureAwait(true)).Should().BeTrue();
    _host.Provider.SetFrame(null);
    await WaitUntilStaleAsync(sessionId).ConfigureAwait(true);
    var sequenceId = await _host.CreateInlineWaitSequenceAsync(WaitForImageCaptureTestHost.ImageId, 4000, 0.85).ConfigureAwait(true);

    var run = _host.ExecuteSequenceFirstStepAsync(sequenceId, sessionId);
    await Task.Delay(400).ConfigureAwait(true);
    _host.Provider.SetFrame(_host.ImageFramePng);
    var (step, _) = await run.ConfigureAwait(true);

    step.GetProperty("actionOutcome").GetString().Should().Be("image_detected");
    var details = step.GetProperty("waitForImageDetails");
    details.GetProperty("stalePassCount").GetInt32().Should().BeGreaterThan(0);
    var hasReason = details.TryGetProperty("staleReason", out var reason) && reason.ValueKind != JsonValueKind.Null;
    hasReason.Should().BeFalse("the step did not time out");
  }
}
