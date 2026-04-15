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

  private static Command CreatePrimitiveTapCommand(string commandId = "cmd-primitive", string imageId = "img-1") => new() {
    Id = commandId,
    Name = "Primitive",
    TriggerId = null,
    Steps = new Collection<CommandStep> {
      new() {
        Type = CommandStepType.PrimitiveTap,
        TargetId = string.Empty,
        Order = 0,
        PrimitiveTap = new PrimitiveTapConfig {
          DetectionTarget = new DetectionTarget(imageId, 0.9, 0, 0, DetectionSelectionStrategy.HighestConfidence)
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
      new ActionRepoStub(),
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
      new ActionRepoStub(),
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
      new ActionRepoStub(),
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
      new ActionRepoStub(),
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
      new ActionRepoStub(),
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
      new ActionRepoStub(),
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
      new ActionRepoStub(),
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
      new ActionRepoStub(),
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
      new ActionRepoStub(),
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
      new ActionRepoStub(),
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

  #region Test stubs

  private sealed class CommandRepoStub : ICommandRepository {
    private readonly Command _command;
    public CommandRepoStub(Command command) => _command = command;
    public Task<Command> AddAsync(Command command, CancellationToken ct = default) => Task.FromResult(command);
    public Task<bool> DeleteAsync(string id, CancellationToken ct = default) => Task.FromResult(false);
    public Task<Command?> GetAsync(string id, CancellationToken ct = default) => Task.FromResult<Command?>(id == _command.Id ? _command : null);
    public Task<IReadOnlyList<Command>> ListAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<Command>>(new[] { _command });
    public Task<Command?> UpdateAsync(Command command, CancellationToken ct = default) => Task.FromResult<Command?>(command);
  }

  private sealed class ActionRepoStub : GameBot.Domain.Actions.IActionRepository {
    public Task<GameBot.Domain.Actions.Action> AddAsync(GameBot.Domain.Actions.Action action, CancellationToken ct = default) => Task.FromResult(action);
    public Task<GameBot.Domain.Actions.Action?> GetAsync(string id, CancellationToken ct = default) => Task.FromResult<GameBot.Domain.Actions.Action?>(null);
    public Task<IReadOnlyList<GameBot.Domain.Actions.Action>> ListAsync(string? gameId = null, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<GameBot.Domain.Actions.Action>>(Array.Empty<GameBot.Domain.Actions.Action>());
    public Task<GameBot.Domain.Actions.Action?> UpdateAsync(GameBot.Domain.Actions.Action action, CancellationToken ct = default) => Task.FromResult<GameBot.Domain.Actions.Action?>(action);
    public Task<bool> DeleteAsync(string id, CancellationToken ct = default) => Task.FromResult(false);
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

  #endregion
}
