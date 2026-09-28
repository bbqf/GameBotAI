using System.Collections.ObjectModel;
using System.Drawing;
using System.Drawing.Imaging;
using FluentAssertions;
using GameBot.Domain.Commands;
using GameBot.Domain.Config;
using GameBot.Domain.Services;
using GameBot.Domain.Sessions;
using GameBot.Domain.Triggers;
using GameBot.Domain.Triggers.Evaluators;
using GameBot.Domain.Vision;
using GameBot.Emulator.Session;
using GameBot.Service.Services;
using Microsoft.Extensions.Logging.Abstractions;
using OpenCvSharp;
using Xunit;

namespace GameBot.UnitTests.Commands;

public sealed class CommandExecutorPrimitiveTapTests {
  private static Bitmap CreateOneByOneBitmap() {
    var bmp = new Bitmap(1, 1);
    bmp.SetPixel(0, 0, Color.Red);
    return bmp;
  }

  private static Command CreatePrimitiveTapCommand(string commandId = "cmd-primitive", string imageId = "img-1", int? holdMs = null) => new() {
    Id = commandId,
    Name = "Primitive",
    TriggerId = null,
    Steps = new Collection<CommandStep> {
      new() {
        Type = CommandStepType.PrimitiveTap,
        TargetId = string.Empty,
        Order = 0,
        PrimitiveTap = new PrimitiveTapConfig {
          DetectionTarget = new DetectionTarget(imageId, 0.9, 0, 0, DetectionSelectionStrategy.HighestConfidence),
          HoldMs = holdMs
        }
      }
    }
  };

  private static Command CreatePrimitiveTapCommandNoDetection(string commandId = "cmd-no-detect") => new() {
    Id = commandId,
    Name = "NoDetect",
    TriggerId = null,
    Steps = new Collection<CommandStep> {
      new() {
        Type = CommandStepType.PrimitiveTap,
        TargetId = string.Empty,
        Order = 0,
        PrimitiveTap = null
      }
    }
  };

  [Fact]
  public async Task ForceExecuteDetailedAsyncReturnsSkippedInvalidConfigWhenDetectionServicesUnavailable() {
    var command = CreatePrimitiveTapCommand();

    var executor = new CommandExecutor(
      new CommandRepoStub(command),
      new SessionManagerStub(),
      new TriggerRepoStub(),
      new TriggerEvaluationService(Array.Empty<ITriggerEvaluator>()),
      NullLogger<CommandExecutor>.Instance,
      new SessionContextCache());

    var result = await executor.ForceExecuteDetailedAsync("sess-1", command.Id, CancellationToken.None);

    result.Accepted.Should().Be(0);
    result.StepOutcomes.Should().HaveCount(1);
    result.StepOutcomes[0].StepOrder.Should().Be(0);
    result.StepOutcomes[0].Status.Should().Be("skipped_invalid_config");
    result.StepOutcomes[0].Reason.Should().Be("services_unavailable");
  }

  [Fact]
  public async Task PrimitiveTapDetectsOnFirstAttemptAfterInitialWait() {
    var command = CreatePrimitiveTapCommand();
    using var bmp = CreateOneByOneBitmap();

    // Screen source always returns a bitmap (detection succeeds on 1st attempt)
    var screenSource = new ScreenSourceStub(bmp);
    var imageStore = new ReferenceImageStoreStub(("img-1", bmp));
    var matcher = new TemplateMatcherStub(new[] { new TemplateMatch(new BoundingBox(0, 0, 1, 1), 0.95) });
    var config = new AppConfig { CaptureIntervalMs = 50, TapRetryCount = 3, TapRetryProgression = 1.0 };

    var executor = new CommandExecutor(
      new CommandRepoStub(command),
      new SessionManagerStub(),
      new TriggerRepoStub(),
      new TriggerEvaluationService(Array.Empty<ITriggerEvaluator>()),
      NullLogger<CommandExecutor>.Instance,
      imageStore, screenSource, matcher,
      new SessionContextCache(),
      config);

    var result = await executor.ForceExecuteDetailedAsync("sess-1", command.Id, CancellationToken.None);

    result.Accepted.Should().Be(1);
    result.StepOutcomes.Should().HaveCount(1);
    result.StepOutcomes[0].Status.Should().Be("executed");
    result.StepOutcomes[0].Reason.Should().BeNull();
  }

  [Fact]
  public async Task PrimitiveTapDetectsOnThirdAttemptAfterRetries() {
    var command = CreatePrimitiveTapCommand();
    using var bmp = CreateOneByOneBitmap();

    // Return no match on attempts 1 and 2, match on attempt 3
    var matcher = new TemplateMatcherStub(new[] { new TemplateMatch(new BoundingBox(0, 0, 1, 1), 0.95) }, failCount: 2);
    var screenSource = new ScreenSourceStub(bmp);
    var imageStore = new ReferenceImageStoreStub(("img-1", bmp));
    var config = new AppConfig { CaptureIntervalMs = 50, TapRetryCount = 3, TapRetryProgression = 1.0 };

    var executor = new CommandExecutor(
      new CommandRepoStub(command),
      new SessionManagerStub(),
      new TriggerRepoStub(),
      new TriggerEvaluationService(Array.Empty<ITriggerEvaluator>()),
      NullLogger<CommandExecutor>.Instance,
      imageStore, screenSource, matcher,
      new SessionContextCache(),
      config);

    var result = await executor.ForceExecuteDetailedAsync("sess-1", command.Id, CancellationToken.None);

    result.Accepted.Should().Be(1);
    result.StepOutcomes.Should().HaveCount(1);
    result.StepOutcomes[0].Status.Should().Be("executed");
    result.StepOutcomes[0].Reason.Should().Be("detected_after_2_retries");
  }

  [Fact]
  public async Task PrimitiveTapExhaustsRetriesAndFails() {
    var command = CreatePrimitiveTapCommand();
    using var bmp = CreateOneByOneBitmap();

    // Never match — all attempts fail
    var matcher = new TemplateMatcherStub(Array.Empty<TemplateMatch>());
    var screenSource = new ScreenSourceStub(bmp);
    var imageStore = new ReferenceImageStoreStub(("img-1", bmp));
    var config = new AppConfig { CaptureIntervalMs = 50, TapRetryCount = 3, TapRetryProgression = 1.0 };

    var executor = new CommandExecutor(
      new CommandRepoStub(command),
      new SessionManagerStub(),
      new TriggerRepoStub(),
      new TriggerEvaluationService(Array.Empty<ITriggerEvaluator>()),
      NullLogger<CommandExecutor>.Instance,
      imageStore, screenSource, matcher,
      new SessionContextCache(),
      config);

    var result = await executor.ForceExecuteDetailedAsync("sess-1", command.Id, CancellationToken.None);

    result.Accepted.Should().Be(0);
    result.StepOutcomes.Should().HaveCount(1);
    result.StepOutcomes[0].Status.Should().Be("skipped_detection_failed");
    result.StepOutcomes[0].Reason.Should().Be("detection_failed_after_3_retries");
  }

  [Fact]
  public async Task PrimitiveTapCancellationDuringWaitReportsCancelled() {
    var command = CreatePrimitiveTapCommand();
    using var bmp = CreateOneByOneBitmap();

    // Never match, so the loop keeps going until cancelled
    var matcher = new TemplateMatcherStub(Array.Empty<TemplateMatch>());
    var screenSource = new ScreenSourceStub(bmp);
    var imageStore = new ReferenceImageStoreStub(("img-1", bmp));
    var config = new AppConfig { CaptureIntervalMs = 50, TapRetryCount = 100, TapRetryProgression = 1.0 };

    var executor = new CommandExecutor(
      new CommandRepoStub(command),
      new SessionManagerStub(),
      new TriggerRepoStub(),
      new TriggerEvaluationService(Array.Empty<ITriggerEvaluator>()),
      NullLogger<CommandExecutor>.Instance,
      imageStore, screenSource, matcher,
      new SessionContextCache(),
      config);

    using var cts = new CancellationTokenSource();
    cts.CancelAfter(TimeSpan.FromMilliseconds(200));

    var result = await executor.ForceExecuteDetailedAsync("sess-1", command.Id, cts.Token);

    result.Accepted.Should().Be(0);
    result.StepOutcomes.Should().HaveCount(1);
    result.StepOutcomes[0].Status.Should().Be("cancelled");
    result.StepOutcomes[0].Reason.Should().StartWith("cancelled_during_retry_");
  }

  [Fact]
  public async Task PrimitiveTapCountZeroSingleCheckNoRetries() {
    var command = CreatePrimitiveTapCommand();
    using var bmp = CreateOneByOneBitmap();

    // No match — with COUNT=0, should fail immediately after single check
    var matcher = new TemplateMatcherStub(Array.Empty<TemplateMatch>());
    var screenSource = new ScreenSourceStub(bmp);
    var imageStore = new ReferenceImageStoreStub(("img-1", bmp));
    var config = new AppConfig { CaptureIntervalMs = 50, TapRetryCount = 0, TapRetryProgression = 1.0 };

    var executor = new CommandExecutor(
      new CommandRepoStub(command),
      new SessionManagerStub(),
      new TriggerRepoStub(),
      new TriggerEvaluationService(Array.Empty<ITriggerEvaluator>()),
      NullLogger<CommandExecutor>.Instance,
      imageStore, screenSource, matcher,
      new SessionContextCache(),
      config);

    var result = await executor.ForceExecuteDetailedAsync("sess-1", command.Id, CancellationToken.None);

    result.Accepted.Should().Be(0);
    result.StepOutcomes.Should().HaveCount(1);
    result.StepOutcomes[0].Status.Should().Be("skipped_detection_failed");
    result.StepOutcomes[0].Reason.Should().Be("detection_failed_after_0_retries");
  }

  [Fact]
  public async Task PrimitiveTapWithoutDetectionTargetIsUnaffectedByRetryLogic() {
    var command = CreatePrimitiveTapCommandNoDetection();

    // Use the full constructor with detection services — the lack of DetectionTarget should
    // make the retry loop irrelevant and fall through to the existing skip behaviour.
    using var bmp = CreateOneByOneBitmap();
    var screenSource = new ScreenSourceStub(bmp);
    var imageStore = new ReferenceImageStoreStub();
    var matcher = new TemplateMatcherStub(Array.Empty<TemplateMatch>());
    var config = new AppConfig { CaptureIntervalMs = 50, TapRetryCount = 3 };

    var executor = new CommandExecutor(
      new CommandRepoStub(command),
      new SessionManagerStub(),
      new TriggerRepoStub(),
      new TriggerEvaluationService(Array.Empty<ITriggerEvaluator>()),
      NullLogger<CommandExecutor>.Instance,
      imageStore, screenSource, matcher,
      new SessionContextCache(),
      config);

    var result = await executor.ForceExecuteDetailedAsync("sess-1", command.Id, CancellationToken.None);

    result.Accepted.Should().Be(0);
    result.StepOutcomes.Should().HaveCount(1);
    result.StepOutcomes[0].Status.Should().Be("skipped_invalid_config");
    result.StepOutcomes[0].Reason.Should().Be("primitive_tap_missing_detection");
  }

  [Fact]
  public async Task PrimitiveTapProgressionDoublesWaitTimeBetweenRetries() {
    // PROGRESSION=2, WAIT_TIME=100, COUNT=3 — never detect so all retries are exercised.
    // Expected total waits: initial 100ms + retry waits 100ms + 200ms + 400ms = 800ms total.
    // With PROGRESSION=1, total = 100 + 100 + 100 + 100 = 400ms.
    // Assert total time is meaningfully greater with PROGRESSION=2.
    var command = CreatePrimitiveTapCommand();
    using var bmp = CreateOneByOneBitmap();

    var matcher = new TemplateMatcherStub(Array.Empty<TemplateMatch>());
    var screenSource = new ScreenSourceStub(bmp);
    var imageStore = new ReferenceImageStoreStub(("img-1", bmp));
    var config = new AppConfig { CaptureIntervalMs = 100, TapRetryCount = 3, TapRetryProgression = 2.0 };

    var executor = new CommandExecutor(
      new CommandRepoStub(command),
      new SessionManagerStub(),
      new TriggerRepoStub(),
      new TriggerEvaluationService(Array.Empty<ITriggerEvaluator>()),
      NullLogger<CommandExecutor>.Instance,
      imageStore, screenSource, matcher,
      new SessionContextCache(),
      config);

    var sw = System.Diagnostics.Stopwatch.StartNew();
    var result = await executor.ForceExecuteDetailedAsync("sess-1", command.Id, CancellationToken.None);
    sw.Stop();

    result.StepOutcomes.Should().HaveCount(1);
    result.StepOutcomes[0].Status.Should().Be("skipped_detection_failed");
    result.StepOutcomes[0].Reason.Should().Be("detection_failed_after_3_retries");
    // With progression=2: 100 + 100 + 200 + 400 = 800ms minimum
    // Allow generous margin but it should be clearly > 400ms (what progression=1 would take)
    sw.ElapsedMilliseconds.Should().BeGreaterThan(600);
  }

  [Fact]
  public async Task PrimitiveTapProgressionOneYieldsConstantIntervals() {
    // PROGRESSION=1 (default), WAIT_TIME=100, COUNT=3 — never detect.
    // Expected total: 100 + 100 + 100 + 100 = 400ms, all equal intervals.
    var command = CreatePrimitiveTapCommand();
    using var bmp = CreateOneByOneBitmap();

    var matcher = new TemplateMatcherStub(Array.Empty<TemplateMatch>());
    var screenSource = new ScreenSourceStub(bmp);
    var imageStore = new ReferenceImageStoreStub(("img-1", bmp));
    var config = new AppConfig { CaptureIntervalMs = 100, TapRetryCount = 3, TapRetryProgression = 1.0 };

    var executor = new CommandExecutor(
      new CommandRepoStub(command),
      new SessionManagerStub(),
      new TriggerRepoStub(),
      new TriggerEvaluationService(Array.Empty<ITriggerEvaluator>()),
      NullLogger<CommandExecutor>.Instance,
      imageStore, screenSource, matcher,
      new SessionContextCache(),
      config);

    var sw = System.Diagnostics.Stopwatch.StartNew();
    var result = await executor.ForceExecuteDetailedAsync("sess-1", command.Id, CancellationToken.None);
    sw.Stop();

    result.StepOutcomes.Should().HaveCount(1);
    result.StepOutcomes[0].Status.Should().Be("skipped_detection_failed");
    result.StepOutcomes[0].Reason.Should().Be("detection_failed_after_3_retries");
    // With progression=1: 100 + 100 + 100 + 100 = 400ms minimum
    // Should be clearly < 800ms (what progression=2 would take)
    sw.ElapsedMilliseconds.Should().BeGreaterThan(300);
    sw.ElapsedMilliseconds.Should().BeLessThan(800);
  }

  [Fact]
  public async Task PrimitiveTapInvalidProgressionFallsBackToConstant() {
    // AppConfig with invalid progression (0) — should fall back to 1.0 at wiring time.
    // But at domain level, TapRetryProgression = 0 directly. The fallback is in Program.cs.
    // Here we test that the retry loop handles progression=1.0 (the default) correctly.
    var command = CreatePrimitiveTapCommand();
    using var bmp = CreateOneByOneBitmap();

    var matcher = new TemplateMatcherStub(Array.Empty<TemplateMatch>());
    var screenSource = new ScreenSourceStub(bmp);
    var imageStore = new ReferenceImageStoreStub(("img-1", bmp));
    // Use default AppConfig which has TapRetryProgression=1.0
    var config = new AppConfig { CaptureIntervalMs = 50, TapRetryCount = 2 };

    var executor = new CommandExecutor(
      new CommandRepoStub(command),
      new SessionManagerStub(),
      new TriggerRepoStub(),
      new TriggerEvaluationService(Array.Empty<ITriggerEvaluator>()),
      NullLogger<CommandExecutor>.Instance,
      imageStore, screenSource, matcher,
      new SessionContextCache(),
      config);

    var result = await executor.ForceExecuteDetailedAsync("sess-1", command.Id, CancellationToken.None);

    result.StepOutcomes.Should().HaveCount(1);
    result.StepOutcomes[0].Status.Should().Be("skipped_detection_failed");
    result.StepOutcomes[0].Reason.Should().Be("detection_failed_after_2_retries");
  }

  [Fact]
  public async Task PrimitiveTapExecutedPointReflectsJitteredArgsWhileResolvedPointKeepsTarget() {
    var command = CreatePrimitiveTapCommand();
    using var bmp = CreateOneByOneBitmap();

    var screenSource = new ScreenSourceStub(bmp);
    var imageStore = new ReferenceImageStoreStub(("img-1", bmp));
    var matcher = new TemplateMatcherStub(new[] { new TemplateMatch(new BoundingBox(0, 0, 1, 1), 0.95) });
    var config = new AppConfig { CaptureIntervalMs = 50, TapRetryCount = 3, TapRetryProgression = 1.0 };

    // Simulate the real SessionManager's jitter by mutating the dispatched args in place.
    var sessions = new MutatingSessionManagerStub(offsetX: 3, offsetY: 4);

    var executor = new CommandExecutor(
      new CommandRepoStub(command),
      sessions,
      new TriggerRepoStub(),
      new TriggerEvaluationService(Array.Empty<ITriggerEvaluator>()),
      NullLogger<CommandExecutor>.Instance,
      imageStore, screenSource, matcher,
      new SessionContextCache(),
      config);

    var result = await executor.ForceExecuteDetailedAsync("sess-1", command.Id, CancellationToken.None);

    result.StepOutcomes.Should().HaveCount(1);
    var outcome = result.StepOutcomes[0];
    outcome.Status.Should().Be("executed");
    outcome.ResolvedPoint.Should().Be(new PrimitiveTapResolvedPoint(0, 0));
    outcome.ExecutedPoint.Should().Be(new PrimitiveTapResolvedPoint(3, 4));
  }

  [Fact]
  public async Task PrimitiveTapExecutedPointEqualsResolvedPointWhenArgsAreNotMutated() {
    var command = CreatePrimitiveTapCommand();
    using var bmp = CreateOneByOneBitmap();

    var screenSource = new ScreenSourceStub(bmp);
    var imageStore = new ReferenceImageStoreStub(("img-1", bmp));
    var matcher = new TemplateMatcherStub(new[] { new TemplateMatch(new BoundingBox(0, 0, 1, 1), 0.95) });
    var config = new AppConfig { CaptureIntervalMs = 50, TapRetryCount = 3, TapRetryProgression = 1.0 };

    var executor = new CommandExecutor(
      new CommandRepoStub(command),
      new SessionManagerStub(),
      new TriggerRepoStub(),
      new TriggerEvaluationService(Array.Empty<ITriggerEvaluator>()),
      NullLogger<CommandExecutor>.Instance,
      imageStore, screenSource, matcher,
      new SessionContextCache(),
      config);

    var result = await executor.ForceExecuteDetailedAsync("sess-1", command.Id, CancellationToken.None);

    result.StepOutcomes.Should().HaveCount(1);
    var outcome = result.StepOutcomes[0];
    outcome.Status.Should().Be("executed");
    outcome.ExecutedPoint.Should().Be(outcome.ResolvedPoint);
  }

  // Feature 111 (issue #235): a step with a hold duration presses and holds at the detected point.
  [Fact]
  public async Task PrimitiveTapWithHoldDurationSendsAPressAndHoldAtTheDetectedPoint() {
    var command = CreatePrimitiveTapCommand(holdMs: 700);
    var (executor, sessions) = CreateExecutorWithRecordingSessions(command, matchFound: true);

    var result = await executor.ForceExecuteDetailedAsync("sess-1", command.Id, CancellationToken.None);

    result.StepOutcomes.Should().HaveCount(1);
    var outcome = result.StepOutcomes[0];
    outcome.Status.Should().Be("executed");
    outcome.HoldMs.Should().Be(700);
    var input = sessions.Inputs.Should().ContainSingle().Subject;
    input.Type.Should().Be("swipe");
    input.DurationMs.Should().Be(700);
    input.Args["x1"].Should().Be(input.Args["x2"]);
    input.Args["y1"].Should().Be(input.Args["y2"]);
    input.Args["x1"].Should().Be(outcome.ResolvedPoint!.X);
    input.Args["y1"].Should().Be(outcome.ResolvedPoint.Y);
  }

  [Fact]
  public async Task PrimitiveTapWithoutHoldDurationSendsTheTapOfToday() {
    var command = CreatePrimitiveTapCommand();
    var (executor, sessions) = CreateExecutorWithRecordingSessions(command, matchFound: true);

    var result = await executor.ForceExecuteDetailedAsync("sess-1", command.Id, CancellationToken.None);

    result.StepOutcomes[0].Status.Should().Be("executed");
    result.StepOutcomes[0].HoldMs.Should().BeNull();
    var input = sessions.Inputs.Should().ContainSingle().Subject;
    input.Type.Should().Be("swipe");
    input.DurationMs.Should().Be(200);
  }

  [Fact]
  public async Task PrimitiveTapWithHoldDurationSendsNoInputWhenTheImageIsNotFound() {
    var command = CreatePrimitiveTapCommand(holdMs: 700);
    var (executor, sessions) = CreateExecutorWithRecordingSessions(command, matchFound: false);

    var result = await executor.ForceExecuteDetailedAsync("sess-1", command.Id, CancellationToken.None);

    result.Accepted.Should().Be(0);
    result.StepOutcomes[0].Status.Should().Be("skipped_detection_failed");
    result.StepOutcomes[0].HoldMs.Should().BeNull();
    sessions.Inputs.Should().BeEmpty();
  }

  // Feature 112 (issue #222): a "not executed" outcome must mean that no input went to the device.
  [Fact]
  public async Task PrimitiveTapErrorAfterDispatchReportsExecutedWithAcceptedCount() {
    var sessions = new FaultySessionManagerStub(FaultySessionManagerStub.Fault.ReadBackError);
    var executor = CreateStepExecutor(sessions, matchFound: true);

    var result = await executor.ForceExecuteStepAsync("sess-1", CreatePrimitiveTapStep(), CancellationToken.None);

    sessions.SendCalls.Should().Be(1);
    result.Accepted.Should().Be(1);
    var outcome = result.StepOutcomes.Should().ContainSingle().Subject;
    outcome.Status.Should().Be("executed");
    outcome.Reason.Should().Be("executed_then_error");
    outcome.ResolvedPoint.Should().Be(new PrimitiveTapResolvedPoint(0, 0));
    outcome.ExecutedPoint.Should().BeNull();
  }

  [Fact]
  public async Task PrimitiveTapCancellationAfterDispatchReportsExecutedWithAcceptedCount() {
    var sessions = new FaultySessionManagerStub(FaultySessionManagerStub.Fault.ReadBackCancellation);
    var executor = CreateStepExecutor(sessions, matchFound: true);

    var result = await executor.ForceExecuteStepAsync("sess-1", CreatePrimitiveTapStep(), CancellationToken.None);

    sessions.SendCalls.Should().Be(1);
    result.Accepted.Should().Be(1);
    var outcome = result.StepOutcomes.Should().ContainSingle().Subject;
    outcome.Status.Should().Be("executed");
    outcome.Status.Should().NotBe("cancelled").And.NotBe("skipped_detection_failed");
    outcome.Reason.Should().Be("executed_then_cancelled");
    outcome.ResolvedPoint.Should().Be(new PrimitiveTapResolvedPoint(0, 0));
  }

  [Fact]
  public async Task PrimitiveTapErrorDuringDispatchReportsDispatchUnknown() {
    var sessions = new FaultySessionManagerStub(FaultySessionManagerStub.Fault.DispatchError);
    var executor = CreateStepExecutor(sessions, matchFound: true);

    var result = await executor.ForceExecuteStepAsync("sess-1", CreatePrimitiveTapStep(), CancellationToken.None);

    sessions.SendCalls.Should().Be(1);
    result.Accepted.Should().Be(0);
    var outcome = result.StepOutcomes.Should().ContainSingle().Subject;
    outcome.Status.Should().Be("dispatch_unknown");
    outcome.Reason.Should().Be("dispatch_error");
    outcome.ResolvedPoint.Should().Be(new PrimitiveTapResolvedPoint(0, 0));
  }

  [Fact]
  public async Task PrimitiveTapCancellationDuringDispatchReportsDispatchUnknown() {
    var sessions = new FaultySessionManagerStub(FaultySessionManagerStub.Fault.DispatchCancellation);
    var executor = CreateStepExecutor(sessions, matchFound: true);

    var result = await executor.ForceExecuteStepAsync("sess-1", CreatePrimitiveTapStep(), CancellationToken.None);

    sessions.SendCalls.Should().Be(1);
    result.Accepted.Should().Be(0);
    var outcome = result.StepOutcomes.Should().ContainSingle().Subject;
    outcome.Status.Should().Be("dispatch_unknown");
    outcome.Reason.Should().Be("dispatch_cancelled");
    outcome.ResolvedPoint.Should().Be(new PrimitiveTapResolvedPoint(0, 0));
  }

  [Fact]
  public async Task PrimitiveTapDetectionFailureOnEveryAttemptSendsNoInput() {
    var sessions = new FaultySessionManagerStub(FaultySessionManagerStub.Fault.None);
    var executor = CreateStepExecutor(sessions, matchFound: false);

    var result = await executor.ForceExecuteStepAsync("sess-1", CreatePrimitiveTapStep(), CancellationToken.None);

    result.Accepted.Should().Be(0);
    var outcome = result.StepOutcomes.Should().ContainSingle().Subject;
    outcome.Status.Should().Be("skipped_detection_failed");
    outcome.Reason.Should().Be("detection_failed_after_3_retries");
    sessions.SendCalls.Should().Be(0);
  }

  private static CommandStep CreatePrimitiveTapStep() => new() {
    Type = CommandStepType.PrimitiveTap,
    TargetId = string.Empty,
    Order = 0,
    PrimitiveTap = new PrimitiveTapConfig {
      DetectionTarget = new DetectionTarget("img-1", 0.9, 0, 0, DetectionSelectionStrategy.HighestConfidence)
    }
  };

  private static CommandExecutor CreateStepExecutor(ISessionManager sessions, bool matchFound) {
    var bmp = CreateOneByOneBitmap();
    var matches = matchFound ? new[] { new TemplateMatch(new BoundingBox(0, 0, 1, 1), 0.95) } : Array.Empty<TemplateMatch>();
    return new CommandExecutor(
      new CommandRepoStub(CreatePrimitiveTapCommand()),
      sessions,
      new TriggerRepoStub(),
      new TriggerEvaluationService(Array.Empty<ITriggerEvaluator>()),
      NullLogger<CommandExecutor>.Instance,
      new ReferenceImageStoreStub(("img-1", bmp)), new ScreenSourceStub(bmp), new TemplateMatcherStub(matches),
      new SessionContextCache(),
      new AppConfig { CaptureIntervalMs = 10, TapRetryCount = 3, TapRetryProgression = 1.0 });
  }

  private static (CommandExecutor Executor, RecordingSessionManagerStub Sessions) CreateExecutorWithRecordingSessions(Command command, bool matchFound) {
    var bmp = CreateOneByOneBitmap();
    var matches = matchFound ? new[] { new TemplateMatch(new BoundingBox(0, 0, 1, 1), 0.95) } : Array.Empty<TemplateMatch>();
    var sessions = new RecordingSessionManagerStub();
    var executor = new CommandExecutor(
      new CommandRepoStub(command),
      sessions,
      new TriggerRepoStub(),
      new TriggerEvaluationService(Array.Empty<ITriggerEvaluator>()),
      NullLogger<CommandExecutor>.Instance,
      new ReferenceImageStoreStub(("img-1", bmp)), new ScreenSourceStub(bmp), new TemplateMatcherStub(matches),
      new SessionContextCache(),
      new AppConfig { CaptureIntervalMs = 10, TapRetryCount = 1, TapRetryProgression = 1.0 });
    return (executor, sessions);
  }

  #region Test stubs

  /// <summary>Records each dispatched input and does not change its args (no jitter).</summary>
  private sealed class RecordingSessionManagerStub : ISessionManager {
    private readonly EmulatorSession _session = new() { Id = "sess-1", Status = SessionStatus.Running, GameId = "game-1" };
    public List<GameBot.Emulator.Session.InputAction> Inputs { get; } = new();
    public int ActiveCount => 1;
    public bool CanCreateSession => true;
    public EmulatorSession CreateSession(string gameIdOrPath, string? preferredDeviceSerial = null) => _session;
    public EmulatorSession? GetSession(string id) => id == _session.Id ? _session : null;
    public IReadOnlyCollection<EmulatorSession> ListSessions() => new[] { _session };
    public bool StopSession(string id) => true;
    public Task<int> SendInputsAsync(string id, IEnumerable<GameBot.Emulator.Session.InputAction> actions, CancellationToken ct = default) {
      var list = actions.ToList();
      Inputs.AddRange(list);
      return Task.FromResult(list.Count);
    }
    public Task<GameBot.Emulator.Session.SessionInputDispatchResult> SendInputsWithResultsAsync(string id, IEnumerable<GameBot.Emulator.Session.InputAction> actions, CancellationToken ct = default) => Task.FromResult(new GameBot.Emulator.Session.SessionInputDispatchResult(true, Array.Empty<GameBot.Emulator.Session.InputActionResult>()));
    public Task<byte[]> GetSnapshotAsync(string id, CancellationToken ct = default) => Task.FromResult(Array.Empty<byte>());
  }

  private sealed class MutatingSessionManagerStub : ISessionManager {
    private readonly EmulatorSession _session = new() { Id = "sess-1", Status = SessionStatus.Running, GameId = "game-1" };
    private readonly int _offsetX;
    private readonly int _offsetY;
    public MutatingSessionManagerStub(int offsetX, int offsetY) { _offsetX = offsetX; _offsetY = offsetY; }
    public int ActiveCount => 1;
    public bool CanCreateSession => true;
    public EmulatorSession CreateSession(string gameIdOrPath, string? preferredDeviceSerial = null) => _session;
    public EmulatorSession? GetSession(string id) => id == _session.Id ? _session : null;
    public IReadOnlyCollection<EmulatorSession> ListSessions() => new[] { _session };
    public bool StopSession(string id) => true;
    public Task<int> SendInputsAsync(string id, IEnumerable<GameBot.Emulator.Session.InputAction> actions, CancellationToken ct = default) {
      var list = actions.ToList();
      foreach (var a in list) {
        foreach (var key in new[] { "x1", "x2" }) {
          if (a.Args.TryGetValue(key, out var v)) a.Args[key] = Convert.ToInt32(v, System.Globalization.CultureInfo.InvariantCulture) + _offsetX;
        }
        foreach (var key in new[] { "y1", "y2" }) {
          if (a.Args.TryGetValue(key, out var v)) a.Args[key] = Convert.ToInt32(v, System.Globalization.CultureInfo.InvariantCulture) + _offsetY;
        }
      }
      return Task.FromResult(list.Count);
    }
    public Task<GameBot.Emulator.Session.SessionInputDispatchResult> SendInputsWithResultsAsync(string id, IEnumerable<GameBot.Emulator.Session.InputAction> actions, CancellationToken ct = default) => Task.FromResult(new GameBot.Emulator.Session.SessionInputDispatchResult(true, Array.Empty<GameBot.Emulator.Session.InputActionResult>()));
    public Task<byte[]> GetSnapshotAsync(string id, CancellationToken ct = default) => Task.FromResult(Array.Empty<byte>());
  }

  private sealed class CommandRepoStub : ICommandRepository {
    private readonly Command _command;
    public CommandRepoStub(Command command) => _command = command;
    public Task<Command> AddAsync(Command command, CancellationToken ct = default) => Task.FromResult(command);
    public Task<bool> DeleteAsync(string id, CancellationToken ct = default) => Task.FromResult(false);
    public Task<Command?> GetAsync(string id, CancellationToken ct = default) => Task.FromResult<Command?>(id == _command.Id ? _command : null);
    public Task<IReadOnlyList<Command>> ListAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<Command>>(new[] { _command });
    public Task<Command?> UpdateAsync(Command command, CancellationToken ct = default) => Task.FromResult<Command?>(command);
  }

  private sealed class TriggerRepoStub : ITriggerRepository {
    public Task<Trigger?> GetAsync(string id, CancellationToken ct = default) => Task.FromResult<Trigger?>(null);
    public Task UpsertAsync(Trigger trigger, CancellationToken ct = default) => Task.CompletedTask;
    public Task<bool> DeleteAsync(string id, CancellationToken ct = default) => Task.FromResult(false);
    public Task<IReadOnlyList<Trigger>> ListAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<Trigger>>(Array.Empty<Trigger>());
  }

  private sealed class SessionManagerStub : ISessionManager {
    private readonly EmulatorSession _session = new() { Id = "sess-1", Status = SessionStatus.Running, GameId = "game-1" };
    public int ActiveCount => 1;
    public bool CanCreateSession => true;
    public EmulatorSession CreateSession(string gameIdOrPath, string? preferredDeviceSerial = null) => _session;
    public EmulatorSession? GetSession(string id) => id == _session.Id ? _session : null;
    public IReadOnlyCollection<EmulatorSession> ListSessions() => new[] { _session };
    public bool StopSession(string id) => true;
    public Task<int> SendInputsAsync(string id, IEnumerable<GameBot.Emulator.Session.InputAction> actions, CancellationToken ct = default) => Task.FromResult(actions.Count());
    public Task<GameBot.Emulator.Session.SessionInputDispatchResult> SendInputsWithResultsAsync(string id, IEnumerable<GameBot.Emulator.Session.InputAction> actions, CancellationToken ct = default) => Task.FromResult(new GameBot.Emulator.Session.SessionInputDispatchResult(true, Array.Empty<GameBot.Emulator.Session.InputActionResult>()));
    public Task<byte[]> GetSnapshotAsync(string id, CancellationToken ct = default) => Task.FromResult(Array.Empty<byte>());
  }

  private sealed class ScreenSourceStub : IScreenSource {
    private readonly Bitmap? _bitmap;
    public ScreenSourceStub(Bitmap? bitmap) => _bitmap = bitmap;
    public Bitmap? GetLatestScreenshot() => _bitmap;
  }

  private sealed class ReferenceImageStoreStub : IReferenceImageStore {
    private readonly Dictionary<string, Bitmap> _images = new(StringComparer.OrdinalIgnoreCase);
    public ReferenceImageStoreStub(params (string Id, Bitmap Bmp)[] images) { foreach (var (id, bmp) in images) _images[id] = bmp; }
    public bool TryGet(string id, out Bitmap bitmap) { if (_images.TryGetValue(id, out var b)) { bitmap = b; return true; } bitmap = null!; return false; }
    public void AddOrUpdate(string id, Bitmap bitmap) => _images[id] = bitmap;
    public bool Exists(string id) => _images.ContainsKey(id);
    public bool Delete(string id) => _images.Remove(id);
  }

  private sealed class TemplateMatcherStub : ITemplateMatcher {
    private readonly TemplateMatch[] _matches;
    private int _callCount;
    private readonly int _failCount;
    public TemplateMatcherStub(TemplateMatch[] matches, int failCount = 0) { _matches = matches; _failCount = failCount; }
    public List<int> WaitTimesObserved { get; } = new();
    public Task<TemplateMatchResult> MatchAllAsync(Mat screenshot, Mat templateMat, TemplateMatcherConfig config, CancellationToken cancellationToken = default) {
      _callCount++;
      if (_callCount <= _failCount)
        return Task.FromResult(new TemplateMatchResult(Array.Empty<TemplateMatch>(), false));
      return Task.FromResult(new TemplateMatchResult(_matches, false));
    }
  }

  /// <summary>
  /// Feature 112: a session stub that counts the dispatches and makes a given fault. A read-back fault puts a
  /// value in <c>x1</c> that the executor cannot convert, so the fault occurs in the real code after the dispatch.
  /// </summary>
  private sealed class FaultySessionManagerStub : ISessionManager {
    public enum Fault { None, ReadBackError, ReadBackCancellation, DispatchError, DispatchCancellation }

    private readonly EmulatorSession _session = new() { Id = "sess-1", Status = SessionStatus.Running, GameId = "game-1" };
    private readonly Fault _fault;
    public FaultySessionManagerStub(Fault fault) => _fault = fault;
    public int SendCalls { get; private set; }
    public int ActiveCount => 1;
    public bool CanCreateSession => true;
    public EmulatorSession CreateSession(string gameIdOrPath, string? preferredDeviceSerial = null) => _session;
    public EmulatorSession? GetSession(string id) => id == _session.Id ? _session : null;
    public IReadOnlyCollection<EmulatorSession> ListSessions() => new[] { _session };
    public bool StopSession(string id) => true;
    public Task<int> SendInputsAsync(string id, IEnumerable<GameBot.Emulator.Session.InputAction> actions, CancellationToken ct = default) {
      SendCalls++;
      var list = actions.ToList();
      switch (_fault) {
        case Fault.DispatchError:
          throw new InvalidOperationException("adb_failed");
        case Fault.DispatchCancellation:
          throw new OperationCanceledException("dispatch_cancelled");
        case Fault.ReadBackError:
          foreach (var a in list) a.Args["x1"] = new ThrowingConvertible(new InvalidOperationException("read_back_failed"));
          break;
        case Fault.ReadBackCancellation:
          foreach (var a in list) a.Args["x1"] = new ThrowingConvertible(new OperationCanceledException("read_back_cancelled"));
          break;
      }
      return Task.FromResult(list.Count);
    }
    public Task<GameBot.Emulator.Session.SessionInputDispatchResult> SendInputsWithResultsAsync(string id, IEnumerable<GameBot.Emulator.Session.InputAction> actions, CancellationToken ct = default) => Task.FromResult(new GameBot.Emulator.Session.SessionInputDispatchResult(true, Array.Empty<GameBot.Emulator.Session.InputActionResult>()));
    public Task<byte[]> GetSnapshotAsync(string id, CancellationToken ct = default) => Task.FromResult(Array.Empty<byte>());
  }

  /// <summary>A value that throws the given exception for each conversion.</summary>
  private sealed class ThrowingConvertible : IConvertible {
    private readonly Exception _exception;
    public ThrowingConvertible(Exception exception) => _exception = exception;
    public TypeCode GetTypeCode() => TypeCode.Object;
    public bool ToBoolean(IFormatProvider? provider) => throw _exception;
    public byte ToByte(IFormatProvider? provider) => throw _exception;
    public char ToChar(IFormatProvider? provider) => throw _exception;
    public DateTime ToDateTime(IFormatProvider? provider) => throw _exception;
    public decimal ToDecimal(IFormatProvider? provider) => throw _exception;
    public double ToDouble(IFormatProvider? provider) => throw _exception;
    public short ToInt16(IFormatProvider? provider) => throw _exception;
    public int ToInt32(IFormatProvider? provider) => throw _exception;
    public long ToInt64(IFormatProvider? provider) => throw _exception;
    public sbyte ToSByte(IFormatProvider? provider) => throw _exception;
    public float ToSingle(IFormatProvider? provider) => throw _exception;
    public string ToString(IFormatProvider? provider) => throw _exception;
    public object ToType(Type conversionType, IFormatProvider? provider) => throw _exception;
    public ushort ToUInt16(IFormatProvider? provider) => throw _exception;
    public uint ToUInt32(IFormatProvider? provider) => throw _exception;
    public ulong ToUInt64(IFormatProvider? provider) => throw _exception;
  }

  #endregion
}
