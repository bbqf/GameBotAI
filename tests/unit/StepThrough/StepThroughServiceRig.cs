#pragma warning disable CA2007, CA1861, CA1859, CA1849
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GameBot.Domain.Commands;
using GameBot.Domain.Logging;
using GameBot.Domain.Services;
using GameBot.Domain.Services.StepThrough;
using GameBot.Domain.Sessions;
using GameBot.Emulator.Session;
using GameBot.Service.Services;
using GameBot.Service.Services.ExecutionLog;
using GameBot.Service.Services.QueueExecution;
using GameBot.Service.Services.SequenceExecution;
using GameBot.Service.Services.StepThrough;

namespace GameBot.UnitTests.StepThrough;

/// <summary>A time provider that tests move by hand.</summary>
internal sealed class ManualTime : TimeProvider {
  private DateTimeOffset _now = new(2026, 10, 2, 10, 0, 0, TimeSpan.Zero);

  public override DateTimeOffset GetUtcNow() => _now;

  public void Advance(TimeSpan by) => _now += by;
}

internal sealed class InMemorySequences : ISequenceRepository {
  private readonly Dictionary<string, CommandSequence> _items = new(StringComparer.Ordinal);

  public Task<CommandSequence?> GetAsync(string id) => Task.FromResult(_items.TryGetValue(id, out var s) ? Clone(s) : null);

  public Task<IReadOnlyList<CommandSequence>> ListAsync() => Task.FromResult<IReadOnlyList<CommandSequence>>(_items.Values.ToList());

  public Task<CommandSequence> CreateAsync(CommandSequence sequence) { _items[sequence.Id] = sequence; return Task.FromResult(sequence); }

  public Task<CommandSequence> UpdateAsync(CommandSequence sequence) { _items[sequence.Id] = sequence; return Task.FromResult(sequence); }

  public Task<bool> DeleteAsync(string id) => Task.FromResult(_items.Remove(id));

  // The real repository returns a new object for each read. A copy keeps a test from changing the stored object by chance.
  private static CommandSequence Clone(CommandSequence source) {
    var copy = new CommandSequence { Id = source.Id, Name = source.Name, Version = source.Version, UpdatedAt = source.UpdatedAt };
    copy.SetSteps(source.Steps);
    foreach (var parameter in source.Parameters) copy.Parameters.Add(parameter);
    return copy;
  }
}

internal sealed class SessionsStub : ISessionManager {
  private readonly List<EmulatorSession> _sessions = new();

  public EmulatorSession Add(string id, string serial, SessionStatus status = SessionStatus.Running) {
    var session = new EmulatorSession { Id = id, GameId = "queue:q1", Status = status, DeviceSerial = serial };
    _sessions.Add(session);
    return session;
  }

  public int ActiveCount => _sessions.Count;

  public bool CanCreateSession => false;

  public EmulatorSession? GetSession(string id) => _sessions.FirstOrDefault(s => s.Id == id);

  public EmulatorSession CreateSession(string g, string? s = null) => throw new NotSupportedException();

  public IReadOnlyCollection<EmulatorSession> ListSessions() => _sessions.ToArray();

  public bool StopSession(string id) => false;

  public Task<int> SendInputsAsync(string id, IEnumerable<InputAction> actions, CancellationToken ct = default) => Task.FromResult(1);

  public Task<SessionInputDispatchResult> SendInputsWithResultsAsync(string id, IEnumerable<InputAction> actions, CancellationToken ct = default)
    => Task.FromResult(new SessionInputDispatchResult(true, Array.Empty<InputActionResult>()));

  public Task<byte[]> GetSnapshotAsync(string id, CancellationToken ct = default) => Task.FromResult(Array.Empty<byte>());
}

/// <summary>An execution log that records what the service writes.</summary>
internal sealed class RecordingLog : IExecutionLogService {
  public List<ExecutionLogContext> Started { get; } = new();

  public List<(string Id, string Status, string Summary, ExecutionLogContext Context, IReadOnlyList<ExecutionDetailItem>? Details)> Finalized { get; } = new();

  public bool FailWrites { get; set; }

  public Task<string> LogSequenceStartAsync(string sequenceId, string sequenceName, ExecutionLogContext parentContext, CancellationToken ct = default) {
    if (FailWrites) throw new InvalidOperationException("log is down");
    Started.Add(parentContext);
    return Task.FromResult("log-" + Started.Count);
  }

  public Task LogSequenceFinalizeAsync(string executionId, string sequenceId, string sequenceName, string finalStatus, string summary, ExecutionLogContext context, IReadOnlyList<ExecutionDetailItem>? details = null, CancellationToken ct = default) {
    Finalized.Add((executionId, finalStatus, summary, context, details));
    return Task.CompletedTask;
  }

  public Task LogCommandExecutionAsync(string commandId, string commandName, string finalStatus, IReadOnlyList<PrimitiveTapStepOutcome> primitiveOutcomes, string? parentExecutionId, int depth, CancellationToken ct = default) => Task.CompletedTask;

  public Task LogCommandExecutionAsync(string commandId, string commandName, string finalStatus, IReadOnlyList<PrimitiveTapStepOutcome> primitiveOutcomes, ExecutionLogContext context, CancellationToken ct = default) => Task.CompletedTask;

  public Task LogStepExecutionAsync(StepExecutionLogRecord record, CancellationToken ct = default) => Task.CompletedTask;

  public Task LogSequenceExecutionAsync(string sequenceId, string sequenceName, string finalStatus, string summary, string? parentExecutionId, int depth, IReadOnlyList<ExecutionDetailItem>? details = null, CancellationToken ct = default) => Task.CompletedTask;

  public Task LogSequenceExecutionAsync(string sequenceId, string sequenceName, string finalStatus, string summary, ExecutionLogContext context, IReadOnlyList<ExecutionDetailItem>? details = null, CancellationToken ct = default) => Task.CompletedTask;

  public Task<string> LogSequenceStartAsync(string sequenceId, string sequenceName, CancellationToken ct = default) => Task.FromResult("log-root");

  public Task<string> LogQueueStartAsync(string queueId, string queueName, CancellationToken ct = default) => Task.FromResult("q");

  public Task LogQueueFinalizeAsync(string executionId, string queueId, string queueName, string finalStatus, string summary, IReadOnlyList<ExecutionDetailItem>? details = null, CancellationToken ct = default) => Task.CompletedTask;

  public Task<string> LogQueueRotateAsync(string currentRootId, string queueId, string queueName, CancellationToken ct = default) => Task.FromResult("q2");

  public Task LogQueueDeviceFaultAsync(string rootExecutionId, string queueId, string queueName, string reason, CancellationToken ct = default) => Task.CompletedTask;

  public Task<ExecutionSubtreeProjection?> GetSubtreeAsync(string executionId, CancellationToken ct = default) => Task.FromResult<ExecutionSubtreeProjection?>(null);

  public Task<ExecutionLogPage> QueryAsync(ExecutionLogQuery query, CancellationToken ct = default) => Task.FromResult(new ExecutionLogPage(Array.Empty<ExecutionLogEntry>(), null));

  public Task<ExecutionLogEntry?> GetAsync(string id, CancellationToken ct = default) => Task.FromResult<ExecutionLogEntry?>(null);

  public Task<ExecutionLogRetentionPolicy> GetRetentionAsync(CancellationToken ct = default) => throw new NotSupportedException();

  public Task<ExecutionLogRetentionPolicy> UpdateRetentionAsync(bool enabled, int? retentionDays, int? cleanupIntervalMinutes, CancellationToken ct = default) => throw new NotSupportedException();

  public Task<int> CleanupExpiredAsync(CancellationToken ct = default) => Task.FromResult(0);
}

/// <summary>A wiring that a test controls: the test decides what each command does.</summary>
internal sealed class FakeWiring : IStepThroughWiring {
  /// <summary>The commands that ran, in order.</summary>
  public List<string> Executed { get; } = new();

  /// <summary>When set, a command waits for this task or for the cancel of the step.</summary>
  public TaskCompletionSource<bool>? Hold { get; set; }

  public Func<string, Exception?>? Failure { get; set; }

  public Func<GameBot.Domain.Commands.Blocks.Condition, bool> Condition { get; set; } = _ => false;

  public StepWiringRequest? LastRequest { get; private set; }

  /// <summary>True when a command saw a sequence time limit scope. A step-through must have none (FR-005a).</summary>
  public bool TimeLimitScopeSeen { get; private set; }

  public StepperDependencies CreateStepWiring(StepWiringRequest request) {
    LastRequest = request;
    return new StepperDependencies {
      ExecuteCommandAsync = (_, _) => Task.CompletedTask,
      CommandDispatcher = async (commandId, _) => {
        if (SequenceTimeLimitScope.Current is not null) TimeLimitScopeSeen = true;
        if (Hold is { } hold) await hold.Task.WaitAsync(request.Token);
        if (Failure?.Invoke(commandId) is { } failure) throw failure;
        lock (Executed) Executed.Add(commandId);
        return CommandDispatchOutcome.Executed;
      },
      ConditionEvaluator = (condition, _) => Task.FromResult(Condition(condition)),
      PreviewServiceAction = (action, _) => Task.FromResult(new ActionDispatchResult(SequenceStepper.PreviewOutcome, $"would run {action.Type}"))
    };
  }
}

/// <summary>A step-through service with fakes for everything outside it.</summary>
internal sealed class StepThroughServiceRig {
  public const string GameSessionId = "gs1";

  public const string Serial = "emulator-5558";

  public ManualTime Time { get; } = new();

  public InMemorySequences Sequences { get; } = new();

  public SessionsStub Sessions { get; } = new();

  public RecordingLog Log { get; } = new();

  public FakeWiring Wiring { get; } = new();

  public QueueRunRegistry Runs { get; } = new();

  public DeviceClaimRegistry Claims { get; } = new();

  public StepThroughSessionGuard Guard { get; }

  public StepThroughService Service { get; }

  public StepThroughServiceRig() {
    Sessions.Add(GameSessionId, Serial);
    Guard = new StepThroughSessionGuard(Runs, Claims, Time);
    Service = new StepThroughService(
      Sequences,
      Sessions,
      Wiring,
      Log,
      Guard,
      new SequenceRunner(Sequences),
      Time);
  }

  public static SequenceStep Cmd(int order, string id)
    => new() {
      Order = order,
      StepId = id,
      CommandId = id,
      StepType = SequenceStepType.Command
    };

  public async Task<string> SeedAsync(string name, params SequenceStep[] steps) {
    var sequence = new CommandSequence { Id = "seq-" + Guid.NewGuid().ToString("N")[..6], Name = name };
    sequence.SetSteps(steps);
    await Sequences.CreateAsync(sequence);
    return sequence.Id;
  }

  public async Task<string> StartAsync(string sequenceId, string? startPath = null, Dictionary<string, string>? values = null) {
    var result = await Service.StartAsync(new StartStepThroughRequest {
      SequenceId = sequenceId,
      GameSessionId = GameSessionId,
      StartPath = startPath,
      ParameterValues = values
    });
    if (!result.IsSuccess) throw new InvalidOperationException(result.Error!.Code + ": " + result.Error.Message);
    return result.Value!.Id;
  }

  /// <summary>Runs the next step and waits until it ends. Returns the state.</summary>
  public async Task<StepThroughStateDto> RunAsync(string id) {
    var started = await Service.RunNextAsync(id);
    if (!started.IsSuccess) throw new InvalidOperationException(started.Error!.Code + ": " + started.Error.Message);
    var task = Service.RunningTask(id);
    if (task is not null) await task;
    return Service.Get(id).Value!;
  }

  public AttachedQueue AttachQueue(string queueId = "q1", string name = "Daily") {
    var handle = new QueueRunHandle { QueueId = queueId, Cts = new CancellationTokenSource() };
    Runs.TryAdd(queueId, handle);
    Claims.TryClaim(Serial, queueId, name);
    return new AttachedQueue(handle);
  }
}

internal sealed record AttachedQueue(QueueRunHandle Handle);
