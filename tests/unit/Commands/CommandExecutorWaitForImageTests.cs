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
      TimeoutMs = 120,
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
    result.StepOutcomes[0].Status.Should().Be("completed_timeout");
    result.StepOutcomes[0].Reason.Should().Be("timeout_elapsed");
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
