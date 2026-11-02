using System.Drawing;
using System.Globalization;
using GameBot.Domain.Commands;
using GameBot.Domain.Commands.Execution;
using GameBot.Domain.Config;
using GameBot.Domain.Services;
using GameBot.Domain.Vision;
using GameBot.Domain.WaitForStill;
using GameBot.Service.Services.SequenceExecution;

namespace GameBot.Service.Services.WaitForStill;

/// <summary>
/// Runs a <c>WaitForStill</c> step (feature 174). The step takes a frame each capture interval and compares it
/// with the previous frame. The outcome is <c>still</c> when the watched region stays the same for the stable
/// time, or <c>timeout_elapsed</c> at the time limit. A frame that cannot be read is skipped and the wait goes
/// on (as <c>WaitForImage</c> does). A region fully outside the screen gives the result <c>failed</c>. A stop
/// signal passes up as <see cref="OperationCanceledException"/>.
/// </summary>
internal sealed class WaitForStillHandler {
  private const string ScreenUnreadable = "the screen could not be read";

  private readonly ISessionFrameSource? _frames;
  private readonly AppConfig _config;
  private readonly TimeProvider _time;

  public WaitForStillHandler(AppConfig config, ISessionFrameSource? frames = null, TimeProvider? time = null) {
    ArgumentNullException.ThrowIfNull(config);
    _config = config;
    _frames = frames;
    _time = time ?? TimeProvider.System;
  }

  public async Task<ActionDispatchResult> ExecuteAsync(SequenceActionPayload action, string? sessionId, CancellationToken ct) {
    ArgumentNullException.ThrowIfNull(action);
    if (!WaitForStillPayload.TryRead(action, out var payload, out var error) || payload is null) {
      return new ActionDispatchResult("failed", $"WaitForStill step is not valid: {error}");
    }

    var start = _time.GetUtcNow();
    var timeout = TimeSpan.FromMilliseconds(payload.TimeoutMs);
    var deadline = start + timeout;
    if (string.IsNullOrWhiteSpace(sessionId) || _frames is null) {
      await Task.Delay(timeout, _time, ct).ConfigureAwait(false);
      return new ActionDispatchResult(WaitForStillOutcomes.TimeoutElapsed, ScreenUnreadable);
    }

    var interval = TimeSpan.FromMilliseconds(Math.Max(1, _config.CaptureIntervalMs));
    var tracker = new WaitForStillTracker(payload.StableMs, payload.MaxDiff);
    Bitmap? previous = null;
    double? lastDiff = null;
    var anyFrame = false;
    var isLastPass = false;
    try {
      while (true) {
        ct.ThrowIfCancellationRequested();
        var frame = TryCapture(sessionId);
        var now = _time.GetUtcNow();
        if (frame is null) {
          // A failed capture is skipped. The next good frame is a new baseline.
          tracker.Reset();
          previous?.Dispose();
          previous = null;
        }
        else {
          anyFrame = true;
          PixelRegion? area = null;
          if (payload.Region is not null) {
            area = payload.Region.ClipTo(frame.Width, frame.Height);
            if (area is null) {
              var size = $"{frame.Width}x{frame.Height}";
              frame.Dispose();
              return new ActionDispatchResult("failed", $"region is outside the screen ({size})");
            }
          }

          if (previous is null) {
            tracker.Baseline(now);
          }
          else {
            var difference = FrameDifference.Compute(previous, frame, area);
            lastDiff = difference;
            if (tracker.Observe(difference, now)) {
              frame.Dispose();
              return new ActionDispatchResult(
                WaitForStillOutcomes.Still,
                $"still after {Elapsed(start, now)} ms; last difference {Format(difference)}");
            }
          }

          // The new frame is the previous frame of the next pass.
          previous?.Dispose();
          previous = frame;
        }

        if (isLastPass) {
          break;
        }

        var next = WaitForImageSchedule.NextWait(deadline - _time.GetUtcNow(), interval);
        if (next.Stop) {
          break;
        }

        if (next.Wait > TimeSpan.Zero) {
          await Task.Delay(next.Wait, _time, ct).ConfigureAwait(false);
        }

        isLastPass = next.IsLastWait;
      }
    }
    finally {
      previous?.Dispose();
    }

    if (!anyFrame) {
      return new ActionDispatchResult(WaitForStillOutcomes.TimeoutElapsed, ScreenUnreadable);
    }

    return new ActionDispatchResult(
      WaitForStillOutcomes.TimeoutElapsed,
      $"not still after {payload.TimeoutMs} ms; last difference {(lastDiff is null ? "none" : Format(lastDiff.Value))}");
  }

  // A null frame or an exception (not a stop signal) is a failed capture.
  private Bitmap? TryCapture(string sessionId) {
    try {
      return _frames!.Capture(sessionId);
    }
    catch (Exception ex) when (ex is not OperationCanceledException) {
      return null;
    }
  }

  private static long Elapsed(DateTimeOffset start, DateTimeOffset now) =>
    (long)Math.Round((now - start).TotalMilliseconds);

  private static string Format(double value) => value.ToString("0.0000", CultureInfo.InvariantCulture);
}
