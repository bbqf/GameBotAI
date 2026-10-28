using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Drawing;
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

public sealed class CommandExecutorWaitForImageTests {
  private static Bitmap CreateOneByOneBitmap() {
    var bmp = new Bitmap(1, 1);
    bmp.SetPixel(0, 0, Color.Red);
    return bmp;
  }

  private static Command CreateWaitCommand(string commandId = "cmd-wait", WaitForImageConfig? config = null) => new() {
    Id = commandId,
    Name = "Wait",
    TriggerId = null,
    Steps = new Collection<CommandStep> {
      new() {
        Type = CommandStepType.WaitForImage,
        TargetId = string.Empty,
        Order = 0,
        WaitForImage = config ?? new WaitForImageConfig { TimeoutMs = 1000 }
      }
    }
  };

  [Fact]
  public async Task WaitForImageDetectsBeforeTimeoutAndCompletesNormally() {
    using var bmp = CreateOneByOneBitmap();
    var command = CreateWaitCommand(config: new WaitForImageConfig {
      TimeoutMs = 5000,
      DetectionTarget = new DetectionTarget("img-1", 0.9, 0, 0, DetectionSelectionStrategy.HighestConfidence)
    });

    var matcher = new TemplateMatcherStub(new[] { new TemplateMatch(new BoundingBox(0, 0, 1, 1), 0.95) }, failCount: 2);
    var executor = new CommandExecutor(
      new CommandRepoStub(command),
      new SessionManagerStub(),
      new TriggerRepoStub(),
      new TriggerEvaluationService(Array.Empty<ITriggerEvaluator>()),
      NullLogger<CommandExecutor>.Instance,
      new ReferenceImageStoreStub(("img-1", bmp)),
      new ScreenSourceStub(bmp),
      matcher,
      new SessionContextCache(),
      new AppConfig { CaptureIntervalMs = 10 });

    var result = await executor.ForceExecuteDetailedAsync("sess-1", command.Id, CancellationToken.None).ConfigureAwait(false);

    result.Accepted.Should().Be(0);
    result.StepOutcomes.Should().HaveCount(1);
    result.StepOutcomes[0].Status.Should().Be("executed");
    result.StepOutcomes[0].Reason.Should().Be("image_detected");
  }

  [Fact]
  public async Task WaitForImageWithoutConfiguredImageWaitsUntilTimeoutAndCompletesNormally() {
    var command = CreateWaitCommand(config: new WaitForImageConfig { TimeoutMs = 40, DetectionTarget = null });

    var executor = new CommandExecutor(
      new CommandRepoStub(command),
      new SessionManagerStub(),
      new TriggerRepoStub(),
      new TriggerEvaluationService(Array.Empty<ITriggerEvaluator>()),
      NullLogger<CommandExecutor>.Instance,
      new SessionContextCache());

    var stopwatch = Stopwatch.StartNew();
    var result = await executor.ForceExecuteDetailedAsync("sess-1", command.Id, CancellationToken.None).ConfigureAwait(false);
    stopwatch.Stop();

    result.Accepted.Should().Be(0);
    result.StepOutcomes.Should().HaveCount(1);
    // A wait with no image to look for is a plain delay, so waiting it out IS success. Reporting a
    // timeout here marked every command ending in a delay as failed.
    result.StepOutcomes[0].Status.Should().Be("executed");
    result.StepOutcomes[0].Reason.Should().Be("delay_elapsed");
    stopwatch.ElapsedMilliseconds.Should().BeGreaterThanOrEqualTo(25);
  }

  [Fact]
  public async Task WaitForImageWithUnavailableImageStillWaitsAndReportsImageUnavailable() {
    using var bmp = CreateOneByOneBitmap();
    var command = CreateWaitCommand(config: new WaitForImageConfig {
      TimeoutMs = 40,
      DetectionTarget = new DetectionTarget("missing-image", 0.9, 0, 0, DetectionSelectionStrategy.HighestConfidence)
    });

    var executor = new CommandExecutor(
      new CommandRepoStub(command),
      new SessionManagerStub(),
      new TriggerRepoStub(),
      new TriggerEvaluationService(Array.Empty<ITriggerEvaluator>()),
      NullLogger<CommandExecutor>.Instance,
      new ReferenceImageStoreStub(),
      new ScreenSourceStub(bmp),
      new TemplateMatcherStub(Array.Empty<TemplateMatch>()),
      new SessionContextCache(),
      new AppConfig { CaptureIntervalMs = 10 });

    var stopwatch = Stopwatch.StartNew();
    var result = await executor.ForceExecuteDetailedAsync("sess-1", command.Id, CancellationToken.None).ConfigureAwait(false);
    stopwatch.Stop();

    result.Accepted.Should().Be(0);
    result.StepOutcomes.Should().HaveCount(1);
    result.StepOutcomes[0].Status.Should().Be("completed_image_unavailable");
    result.StepOutcomes[0].Reason.Should().Be("image_unavailable");
    stopwatch.ElapsedMilliseconds.Should().BeGreaterThanOrEqualTo(25);
  }

  [Fact]
  public async Task WaitForImageStartsNoPassAtOrAfterTheDeadline() {
    // Feature 166 (FR-010): each pass takes 150 ms and finds nothing. With timeoutMs 300 and a poll
    // interval of 1000 ms, the step does two passes, and the last wait ends 50 ms before the deadline.
    using var bmp = CreateOneByOneBitmap();
    var command = CreateWaitCommand(config: new WaitForImageConfig {
      TimeoutMs = 300,
      DetectionTarget = new DetectionTarget("img-1", 0.9, 0, 0, DetectionSelectionStrategy.HighestConfidence)
    });
    var screen = new SlowNullScreenSource(150);
    var executor = new CommandExecutor(
      new CommandRepoStub(command),
      new SessionManagerStub(),
      new TriggerRepoStub(),
      new TriggerEvaluationService(Array.Empty<ITriggerEvaluator>()),
      NullLogger<CommandExecutor>.Instance,
      new ReferenceImageStoreStub(("img-1", bmp)),
      screen,
      new TemplateMatcherStub(Array.Empty<TemplateMatch>()),
      new SessionContextCache(),
      new AppConfig { CaptureIntervalMs = 1000 });

    var result = await executor.ForceExecuteDetailedAsync("sess-1", command.Id, CancellationToken.None).ConfigureAwait(false);

    result.StepOutcomes[0].Status.Should().Be("completed_timeout");
    result.StepOutcomes[0].Reason.Should().Be("timeout_elapsed");
    var offsets = screen.StartOffsetsMs;
    offsets.Should().HaveCount(2);
    // timeoutMs - 50 ms + 30 ms tolerance for the timer resolution.
    offsets.Should().OnlyContain(offset => offset < 280);
  }

  [Fact]
  public async Task WaitForImageTimeoutWritesOneDebugLine() {
    // Feature 166 (FR-012): one debug line on timeout, with the frame state and the best score.
    using var bmp = CreateOneByOneBitmap();
    var command = CreateWaitCommand(config: new WaitForImageConfig {
      TimeoutMs = 60,
      DetectionTarget = new DetectionTarget("img-1", 0.9, 0, 0, DetectionSelectionStrategy.HighestConfidence)
    });
    var logger = new CapturingLogger();
    var executor = new CommandExecutor(
      new CommandRepoStub(command),
      new SessionManagerStub(),
      new TriggerRepoStub(),
      new TriggerEvaluationService(Array.Empty<ITriggerEvaluator>()),
      logger,
      new ReferenceImageStoreStub(("img-1", bmp)),
      new ScreenSourceStub(null),
      new TemplateMatcherStub(Array.Empty<TemplateMatch>()),
      new SessionContextCache(),
      new AppConfig { CaptureIntervalMs = 20 });

    var result = await executor.ForceExecuteDetailedAsync("sess-1", command.Id, CancellationToken.None).ConfigureAwait(false);

    result.StepOutcomes[0].Status.Should().Be("completed_timeout");
    var lines = logger.Entries
      .Where(e => e.Level == Microsoft.Extensions.Logging.LogLevel.Debug && e.Message.Contains("timed out", StringComparison.Ordinal))
      .ToList();
    lines.Should().ContainSingle();
    var line = lines[0].Message;
    line.Should().Contain("sess-1");
    line.Should().Contain("img-1");
    line.Should().Contain("0.9");
    line.Should().MatchRegex(@"\d+ passes");
    line.Should().Contain("frame available false");
    line.Should().Contain("best score none");
  }

  private sealed class SlowNullScreenSource : IScreenSource {
    private readonly int _passMs;
    private readonly Stopwatch _clock = new();
    private readonly List<long> _offsets = new();
    public SlowNullScreenSource(int passMs) => _passMs = passMs;
    public IReadOnlyList<long> StartOffsetsMs { get { lock (_offsets) return _offsets.ToList(); } }
    public Bitmap? GetLatestScreenshot() {
      // The first pass is the start of the step.
      if (!_clock.IsRunning) _clock.Start();
      lock (_offsets) _offsets.Add(_clock.ElapsedMilliseconds);
      Thread.Sleep(_passMs);
      return null;
    }
  }

  private sealed class CapturingLogger : Microsoft.Extensions.Logging.ILogger<CommandExecutor> {
    private readonly List<(Microsoft.Extensions.Logging.LogLevel Level, string Message)> _entries = new();
    public IReadOnlyList<(Microsoft.Extensions.Logging.LogLevel Level, string Message)> Entries { get { lock (_entries) return _entries.ToList(); } }
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
    public bool IsEnabled(Microsoft.Extensions.Logging.LogLevel logLevel) => true;
    public void Log<TState>(Microsoft.Extensions.Logging.LogLevel logLevel, Microsoft.Extensions.Logging.EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) {
      lock (_entries) _entries.Add((logLevel, formatter(state, exception)));
    }
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
    public Task<TemplateMatchResult> MatchAllAsync(Mat screenshot, Mat templateMat, TemplateMatcherConfig config, CancellationToken cancellationToken = default) {
      _callCount++;
      if (_callCount <= _failCount) {
        return Task.FromResult(new TemplateMatchResult(Array.Empty<TemplateMatch>(), false));
      }

      return Task.FromResult(new TemplateMatchResult(_matches, false));
    }
  }
}
