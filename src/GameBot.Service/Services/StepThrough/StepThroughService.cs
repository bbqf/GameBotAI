using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using GameBot.Domain.Commands;
using GameBot.Domain.Logging;
using GameBot.Domain.Services;
using GameBot.Domain.Services.StepThrough;
using GameBot.Domain.Sessions;
using GameBot.Emulator.Session;
using GameBot.Service.Services.ExecutionLog;
using GameBot.Service.Services.SequenceExecution;
using Microsoft.Extensions.Logging;

namespace GameBot.Service.Services.StepThrough;

/// <inheritdoc cref="IStepThroughService"/>
internal sealed class StepThroughService : IStepThroughService {
  /// <summary>How long a step-through lives without a read from the view (FR-012b).</summary>
  public static readonly TimeSpan LeaseDuration = TimeSpan.FromSeconds(90);

  // How long the end of a step-through waits for a cancelled step to stop before it resumes the queue.
  private static readonly TimeSpan CancelWait = TimeSpan.FromSeconds(10);

  private readonly ISequenceRepository _sequences;
  private readonly ISessionManager _sessions;
  private readonly IStepThroughWiring _wiring;
  private readonly IExecutionLogService _log;
  private readonly IStepThroughSessionGuard _guard;
  private readonly SequenceStepper _stepper;
  private readonly TimeProvider _time;
  private readonly IDeviceContextAccessor? _deviceContext;
  private readonly ILogger<StepThroughService>? _logger;

  private readonly ConcurrentDictionary<string, StepThroughSession> _byId = new(StringComparer.Ordinal);
  private readonly ConcurrentDictionary<string, string> _byGameSession = new(StringComparer.Ordinal);

  public StepThroughService(
      ISequenceRepository sequences,
      ISessionManager sessions,
      IStepThroughWiring wiring,
      IExecutionLogService log,
      IStepThroughSessionGuard guard,
      SequenceRunner runner,
      TimeProvider time,
      IDeviceContextAccessor? deviceContext = null,
      ILogger<StepThroughService>? logger = null) {
    _sequences = sequences;
    _sessions = sessions;
    _wiring = wiring;
    _log = log;
    _guard = guard;
    _stepper = new SequenceStepper(runner);
    _time = time;
    _deviceContext = deviceContext;
    _logger = logger;
  }

  // ── Start ─────────────────────────────────────────────────────────────────────────────────────

  public async Task<StepThroughResult<StepThroughStateDto>> StartAsync(StartStepThroughRequest request, CancellationToken ct = default) {
    ArgumentNullException.ThrowIfNull(request);
    if (string.IsNullOrWhiteSpace(request.SequenceId)) {
      return Fail(StepThroughErrorCodes.SequenceNotFound, "The request has no sequence id. Send the id of a saved sequence.");
    }

    var sequence = await _sequences.GetAsync(request.SequenceId).ConfigureAwait(false);
    if (sequence is null) {
      return Fail(StepThroughErrorCodes.SequenceNotFound, $"No saved sequence has the id '{request.SequenceId}'. Save the sequence and try again.");
    }

    var invalid = ValidateSequence(sequence);
    if (invalid is not null) return StepThroughResult<StepThroughStateDto>.Fail(invalid.Code, invalid.Message);

    if (string.IsNullOrWhiteSpace(request.GameSessionId)) {
      return Fail(StepThroughErrorCodes.SessionUnavailable, "The request has no game session. Select a connected game session.");
    }

    var gameSession = _sessions.GetSession(request.GameSessionId);
    if (gameSession is null || gameSession.Status != SessionStatus.Running) {
      return Fail(StepThroughErrorCodes.SessionUnavailable, $"The game session '{request.GameSessionId}' is not connected. Start the session and try again.");
    }

    var parameterError = ValidateParameterNames(sequence, request.ParameterValues?.Keys);
    if (parameterError is not null) return StepThroughResult<StepThroughStateDto>.Fail(parameterError.Code, parameterError.Message);

    var session = new StepThroughSession {
      Id = Guid.NewGuid().ToString("N"),
      GameSessionId = request.GameSessionId,
      DeviceSerial = gameSession.DeviceSerial,
      SequenceId = sequence.Id,
      SequenceName = string.IsNullOrWhiteSpace(sequence.Name) ? sequence.Id : sequence.Name,
      Sequence = sequence,
      Version = ComputeVersion(sequence),
      Nodes = StepPath.Flatten(sequence.Steps),
      LeaseExpiresAt = _time.GetUtcNow() + LeaseDuration
    };
    session.State.Restart(sequence);
    if (request.ParameterValues is not null) {
      foreach (var pair in request.ParameterValues) session.State.ParameterValues[pair.Key] = pair.Value;
    }

    if (!string.IsNullOrWhiteSpace(request.StartPath)) {
      var selected = SequenceStepper.Select(sequence, session.State, request.StartPath);
      if (selected != StepSelectResult.Ok) return SelectFailure(selected, request.StartPath);
    }

    if (!_byGameSession.TryAdd(session.GameSessionId, session.Id)) {
      return Fail(StepThroughErrorCodes.SessionInUse, $"Another step-through already uses the game session '{session.GameSessionId}'. End it first.");
    }

    _byId[session.Id] = session;
    lock (session.Gate) {
      return StepThroughResult<StepThroughStateDto>.Ok(BuildState(session, null));
    }
  }

  // ── Reads ─────────────────────────────────────────────────────────────────────────────────────

  public StepThroughResult<StepThroughStateDto> Get(string id, int? afterSeq = null) {
    if (!TryFind(id, out var session)) return NotFound();
    lock (session.Gate) {
      Touch(session);
      return StepThroughResult<StepThroughStateDto>.Ok(BuildState(session, afterSeq));
    }
  }

  public Task? RunningTask(string id) {
    if (!_byId.TryGetValue(id, out var session)) return null;
    lock (session.Gate) {
      return session.RunTask;
    }
  }

  // ── Run next ──────────────────────────────────────────────────────────────────────────────────

  public async Task<StepThroughResult<StepThroughStateDto>> RunNextAsync(string id, CancellationToken ct = default) {
    if (!TryFind(id, out var session)) return NotFound();
    lock (session.Gate) {
      Touch(session);
      if (session.Running) return StepRunning();
    }

    var changed = await CheckVersionAsync(session).ConfigureAwait(false);
    if (changed is not null) return StepThroughResult<StepThroughStateDto>.Fail(changed.Code, changed.Message);

    lock (session.Gate) {
      if (session.Ended) return NotFound();
      if (session.Running) return StepRunning();
      if (session.State.Cursor is null) {
        return Fail(StepThroughErrorCodes.SequenceComplete, "The sequence is complete. Select a step to run it again, or restart the step-through.");
      }

      var gameSession = _sessions.GetSession(session.GameSessionId);
      if (gameSession is null || gameSession.Status != SessionStatus.Running) {
        return Fail(StepThroughErrorCodes.SessionUnavailable, $"The game session '{session.GameSessionId}' is not connected. Start the session and try again.");
      }

      var queue = _guard.Inspect(session.DeviceSerial);
      if (queue.State is SessionQueueState.Running or SessionQueueState.FiringActive) {
        var canPause = queue.State == SessionQueueState.Running;
        return StepThroughResult<StepThroughStateDto>.Fail(
          StepThroughErrorCodes.QueueRunning,
          canPause
            ? $"The queue '{queue.QueueName}' runs on this device. A step could collide with a firing of the queue. Pause the queue, then run the step."
            : $"The queue '{queue.QueueName}' runs a firing on this device now. Wait for the firing to end, then run the step.",
          new Dictionary<string, object?> {
            ["queueId"] = queue.QueueId,
            ["queueName"] = queue.QueueName,
            ["canPause"] = canPause
          });
      }

      session.Running = true;
      session.RunningPath = session.State.Cursor;
      session.RunningStartedAt = _time.GetUtcNow();
      var cts = new CancellationTokenSource();
      session.RunCts = cts;
      // The step runs in the background and does not use the token of the request.
      session.RunTask = Task.Run(() => ExecuteStepAsync(session, cts), CancellationToken.None);
      return StepThroughResult<StepThroughStateDto>.Ok(BuildState(session, null));
    }
  }

  private async Task ExecuteStepAsync(StepThroughSession session, CancellationTokenSource cts) {
    var token = cts.Token;
    var started = _time.GetUtcNow();
    var stopwatch = System.Diagnostics.Stopwatch.StartNew();
    var rootId = await TryStartLogAsync(session).ConfigureAwait(false);

    StepperState work;
    CommandSequence sequence;
    lock (session.Gate) {
      work = session.State.Clone();
      sequence = session.Sequence;
    }

    IReadOnlyList<HistoryEntry> produced = Array.Empty<HistoryEntry>();
    var cancelled = false;
    string? failure = null;
    try {
      using var deviceScope = _deviceContext?.Push(DeviceContext.For(session.GameSessionId, session.DeviceSerial));
      var wiring = _wiring.CreateStepWiring(new StepWiringRequest(session.SequenceId, session.SequenceName, session.GameSessionId, rootId, token));
      produced = await _stepper.RunNextAsync(sequence, work, wiring, token).ConfigureAwait(false);
    }
    catch (OperationCanceledException) {
      cancelled = true;
    }
    catch (Exception ex) {
      failure = ex.Message;
      if (_logger is not null) StepThroughLog.StepFailed(_logger, session.Id, ex);
    }

    var committed = new List<HistoryEntry>();
    lock (session.Gate) {
      if (cancelled || failure is not null) {
        // The state of the stepper stays as it was before the step. The cursor stays on the step.
        committed.Add(session.State.Append(new HistoryEntry(
          0,
          session.State.Cursor ?? string.Empty,
          null,
          HistoryKind.Step,
          null,
          cancelled ? "Cancelled" : "Failed",
          cancelled ? "cancelled" : "failed",
          cancelled ? "The step was cancelled." : $"The step-through could not run the step: {failure}",
          Array.Empty<string>(),
          Array.Empty<string>(),
          started,
          (int)stopwatch.ElapsedMilliseconds)));
      }
      else {
        session.State.Adopt(work);
        committed.AddRange(produced);
      }

      session.Running = false;
      session.RunningPath = null;
      session.RunCts = null;
    }

    await FinishLogAsync(session, rootId, committed).ConfigureAwait(false);
    cts.Dispose();
  }

  // ── Select, cancel, restart, values, queue ────────────────────────────────────────────────────

  public async Task<StepThroughResult<StepThroughStateDto>> SelectAsync(string id, string? path, CancellationToken ct = default) {
    if (!TryFind(id, out var session)) return NotFound();
    lock (session.Gate) {
      Touch(session);
      if (session.Running) return StepRunning();
    }

    var changed = await CheckVersionAsync(session).ConfigureAwait(false);
    if (changed is not null) return StepThroughResult<StepThroughStateDto>.Fail(changed.Code, changed.Message);

    lock (session.Gate) {
      if (session.Running) return StepRunning();
      var result = SequenceStepper.Select(session.Sequence, session.State, path);
      return result == StepSelectResult.Ok
        ? StepThroughResult<StepThroughStateDto>.Ok(BuildState(session, null))
        : SelectFailure(result, path);
    }
  }

  public StepThroughResult<CancelOutcome> Cancel(string id) {
    if (!TryFind(id, out var session)) {
      return StepThroughResult<CancelOutcome>.Fail(StepThroughErrorCodes.StepThroughNotFound, NotFoundMessage);
    }

    CancellationTokenSource? cts = null;
    bool wasRunning;
    lock (session.Gate) {
      Touch(session);
      wasRunning = session.Running;
      if (wasRunning) cts = session.RunCts;
    }

    // The cancel runs outside the lock. The callbacks of the token can run the end of the step on this thread.
    // The end of the step takes the lock too. Under the lock it would run inside the lock of this call.
    if (cts is not null) {
      try {
        cts.Cancel();
      }
      catch (ObjectDisposedException) {
        // The step ended at the same time. There is nothing to cancel.
      }
    }

    lock (session.Gate) {
      return StepThroughResult<CancelOutcome>.Ok(new CancelOutcome(BuildState(session, null), wasRunning));
    }
  }

  public async Task<StepThroughResult<StepThroughStateDto>> RestartAsync(string id, CancellationToken ct = default) {
    if (!TryFind(id, out var session)) return NotFound();
    lock (session.Gate) {
      Touch(session);
      if (session.Running) return StepRunning();
    }

    // A restart also takes the stored version again, so the author can go on after a change (FR-013).
    var current = await _sequences.GetAsync(session.SequenceId).ConfigureAwait(false);
    if (current is null) {
      return Fail(StepThroughErrorCodes.SequenceNotFound, $"The sequence '{session.SequenceId}' does not exist any more. End the step-through.");
    }

    var invalid = ValidateSequence(current);
    if (invalid is not null) return StepThroughResult<StepThroughStateDto>.Fail(invalid.Code, invalid.Message);

    lock (session.Gate) {
      if (session.Running) return StepRunning();
      session.Sequence = current;
      session.SequenceName = string.IsNullOrWhiteSpace(current.Name) ? current.Id : current.Name;
      session.Version = ComputeVersion(current);
      session.Nodes = StepPath.Flatten(current.Steps);
      session.State.Restart(current);
      return StepThroughResult<StepThroughStateDto>.Ok(BuildState(session, null));
    }
  }

  public StepThroughResult<StepThroughStateDto> SetValues(string id, SetValuesRequest request) {
    ArgumentNullException.ThrowIfNull(request);
    if (!TryFind(id, out var session)) return NotFound();
    lock (session.Gate) {
      Touch(session);
      if (session.Running) return StepRunning();

      var parameterError = ValidateParameterNames(session.Sequence, request.ParameterValues?.Keys);
      if (parameterError is not null) return StepThroughResult<StepThroughStateDto>.Fail(parameterError.Code, parameterError.Message);

      var knownStepIds = session.Nodes.Where(n => n.StepId is not null).Select(n => n.StepId!).ToHashSet(StringComparer.OrdinalIgnoreCase);
      foreach (var key in request.Outcomes?.Keys ?? Enumerable.Empty<string>()) {
        if (!knownStepIds.Contains(key)) {
          return Fail(StepThroughErrorCodes.UnknownStep, $"The sequence has no step with the id '{key}'. Use the id of a step of this sequence.");
        }
      }

      session.State.ParameterValues.Clear();
      foreach (var pair in request.ParameterValues ?? new Dictionary<string, string>()) session.State.ParameterValues[pair.Key] = pair.Value;

      // An outcome with a value sets the outcome. An outcome with no value makes it "not set".
      foreach (var pair in request.Outcomes ?? new Dictionary<string, string?>()) {
        if (string.IsNullOrWhiteSpace(pair.Value)) session.State.Outcomes.Remove(pair.Key);
        else session.State.Outcomes[pair.Key] = pair.Value;
      }

      return StepThroughResult<StepThroughStateDto>.Ok(BuildState(session, null));
    }
  }

  public StepThroughResult<StepThroughStateDto> PauseQueue(string id) {
    if (!TryFind(id, out var session)) return NotFound();
    lock (session.Gate) {
      Touch(session);
      var outcome = _guard.TryPause(session.DeviceSerial, out var status);
      switch (outcome) {
        case QueuePauseOutcome.NoQueue:
          return Fail(StepThroughErrorCodes.NoOwningQueue, "No queue runs on the device of this game session. There is nothing to pause.");
        case QueuePauseOutcome.FiringActive:
          return StepThroughResult<StepThroughStateDto>.Fail(
            StepThroughErrorCodes.QueueRunActive,
            $"The queue '{status.QueueName}' runs a firing on this device now. Wait for the firing to end, then pause the queue.",
            new Dictionary<string, object?> { ["queueId"] = status.QueueId, ["queueName"] = status.QueueName });
        case QueuePauseOutcome.Paused:
          session.PausedQueueId = status.QueueId;
          break;
      }

      return StepThroughResult<StepThroughStateDto>.Ok(BuildState(session, null));
    }
  }

  // ── End and lease ─────────────────────────────────────────────────────────────────────────────

  public async Task EndAsync(string id) {
    if (!_byId.TryRemove(id, out var session)) return;
    Task? runTask;
    string? pausedQueueId;
    CancellationTokenSource? runCts = null;
    lock (session.Gate) {
      session.Ended = true;
      if (session.Running) runCts = session.RunCts;
      runTask = session.RunTask;
      pausedQueueId = session.PausedQueueId;
    }

    if (runCts is not null) {
      try {
        await runCts.CancelAsync().ConfigureAwait(false);
      }
      catch (ObjectDisposedException) {
        // The step ended at the same time. There is nothing to cancel.
      }
    }

    // The queue resumes after the step stops, so the queue cannot start a firing while a step still runs.
    if (runTask is not null) {
      await Task.WhenAny(runTask, Task.Delay(CancelWait)).ConfigureAwait(false);
    }

    if (pausedQueueId is not null) _guard.Resume(pausedQueueId);
    _byGameSession.TryRemove(new KeyValuePair<string, string>(session.GameSessionId, session.Id));
  }

  public async Task<int> SweepExpiredAsync() {
    var now = _time.GetUtcNow();
    var expired = _byId.Values.Where(s => {
      lock (s.Gate) {
        return s.LeaseExpiresAt <= now;
      }
    }).Select(s => s.Id).ToList();
    foreach (var id in expired) {
      if (_logger is not null) StepThroughLog.LeaseExpired(_logger, id);
      await EndAsync(id).ConfigureAwait(false);
    }

    return expired.Count;
  }

  // ── Helpers ───────────────────────────────────────────────────────────────────────────────────

  private bool TryFind(string id, out StepThroughSession session) {
    session = null!;
    return !string.IsNullOrWhiteSpace(id) && _byId.TryGetValue(id, out session!);
  }

  private void Touch(StepThroughSession session) => session.LeaseExpiresAt = _time.GetUtcNow() + LeaseDuration;

  private static StepThroughResult<StepThroughStateDto> Fail(string code, string message)
    => StepThroughResult<StepThroughStateDto>.Fail(code, message);

  private const string NotFoundMessage = "The step-through ended or its lease expired. Start a new step-through.";

  private static StepThroughResult<StepThroughStateDto> NotFound()
    => Fail(StepThroughErrorCodes.StepThroughNotFound, NotFoundMessage);

  private static StepThroughResult<StepThroughStateDto> StepRunning()
    => Fail(StepThroughErrorCodes.StepRunning, "A step runs now. Wait for it to end, or cancel it.");

  private static StepThroughResult<StepThroughStateDto> SelectFailure(StepSelectResult result, string? path)
    => result == StepSelectResult.NotSelectable
      ? Fail(StepThroughErrorCodes.NotSelectable, $"The row '{path}' is a loop or an if step. Select one of the steps inside it.")
      : Fail(StepThroughErrorCodes.UnknownStep, $"The sequence has no step with the path '{path}'. Select a row of the step list.");

  private static StepThroughError? ValidateSequence(CommandSequence sequence) {
    var isFlowGraph = !string.IsNullOrWhiteSpace(sequence.EntryStepId) && sequence.FlowSteps.Count > 0;
    if (isFlowGraph || sequence.Blocks.Count > 0) {
      return new StepThroughError(
        StepThroughErrorCodes.UnsupportedSequenceKind,
        "This sequence uses a flow graph or blocks. The step-through supports only sequences that are a list of steps.");
    }

    return sequence.Steps.Count == 0
      ? new StepThroughError(StepThroughErrorCodes.SequenceEmpty, "The sequence has no steps. Add a step and save the sequence.")
      : null;
  }

  private static StepThroughError? ValidateParameterNames(CommandSequence sequence, IEnumerable<string>? names) {
    if (names is null) return null;
    var declared = sequence.Parameters.Select(p => p.Name).ToHashSet(StringComparer.Ordinal);
    foreach (var name in names) {
      if (!declared.Contains(name)) {
        return new StepThroughError(
          StepThroughErrorCodes.UnknownParameter,
          $"The sequence has no parameter with the name '{name}'. Use the name of a parameter that the sequence declares.");
      }
    }

    return null;
  }

  /// <summary>The version of a stored sequence: the SHA-256 hash of its JSON (research R10).</summary>
  internal static string ComputeVersion(CommandSequence sequence) {
    var json = JsonSerializer.Serialize(sequence);
    return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json)));
  }

  private async Task<StepThroughError?> CheckVersionAsync(StepThroughSession session) {
    var current = await _sequences.GetAsync(session.SequenceId).ConfigureAwait(false);
    if (current is not null && string.Equals(ComputeVersion(current), session.Version, StringComparison.Ordinal)) {
      return null;
    }

    return new StepThroughError(
      StepThroughErrorCodes.SequenceChanged,
      "The saved sequence changed after the step-through started. Restart the step-through to use the new version.");
  }

  private StepThroughStateDto BuildState(StepThroughSession session, int? afterSeq) {
    var state = session.State;
    var history = afterSeq is { } after ? state.HistoryAfter(after) : state.History;
    var stateName = session.Running ? "running" : state.Cursor is null ? "complete" : "idle";
    var queue = _guard.Inspect(session.DeviceSerial);

    return new StepThroughStateDto {
      Id = session.Id,
      SequenceId = session.SequenceId,
      SequenceName = session.SequenceName,
      GameSessionId = session.GameSessionId,
      State = stateName,
      Cursor = state.Cursor,
      Nodes = session.Nodes.Select(n => new StepNodeDto {
        Path = n.Path,
        Depth = n.Depth,
        StepId = n.StepId,
        Type = n.Type,
        Label = n.Label,
        Container = n.Container,
        Selectable = n.Selectable,
        Branch = n.Branch
      }).ToList(),
      Running = session.Running
        ? new RunningStepDto { Path = session.RunningPath ?? string.Empty, StartedAt = session.RunningStartedAt }
        : null,
      History = history.Select(ToDto).ToList(),
      Parameters = session.Sequence.Parameters.Select(p => new StepThroughParameterDto {
        Name = p.Name,
        Value = state.ParameterValues.TryGetValue(p.Name, out var value) ? value : p.Default,
        IsSet = state.ParameterValues.ContainsKey(p.Name)
      }).ToList(),
      Outcomes = new Dictionary<string, string>(state.Outcomes, StringComparer.OrdinalIgnoreCase),
      Queue = queue.State == SessionQueueState.NoQueue
        ? null
        : new StepThroughQueueDto {
          QueueId = queue.QueueId!,
          QueueName = queue.QueueName ?? queue.QueueId!,
          Running = queue.State is SessionQueueState.Running or SessionQueueState.FiringActive,
          FiringActive = queue.State == SessionQueueState.FiringActive,
          PausedByStepThrough = queue.PausedByStepThrough,
          AlreadyPaused = queue.State == SessionQueueState.Paused && !queue.PausedByStepThrough
        },
      LeaseExpiresAt = session.LeaseExpiresAt
    };
  }

  private static HistoryEntryDto ToDto(HistoryEntry entry) => new() {
    Seq = entry.Seq,
    Path = entry.Path,
    StepId = entry.StepId,
    Kind = entry.Kind switch {
      HistoryKind.Enter => "enter",
      HistoryKind.Exit => "exit",
      _ => "step"
    },
    Iteration = entry.Iteration,
    Status = entry.Status,
    Outcome = entry.Outcome,
    Message = entry.Message,
    Effects = entry.Effects,
    Notes = entry.Notes,
    StartedAt = entry.StartedAt,
    DurationMs = entry.DurationMs,
    ExecutionLogId = entry.ExecutionLogId
  };

  // ── Execution log (FR-016) ────────────────────────────────────────────────────────────────────

  /// <summary>Opens the log entry of one step run. A failure of the log never stops the step.</summary>
  private async Task<string> TryStartLogAsync(StepThroughSession session) {
    try {
      return await _log.LogSequenceStartAsync(
        session.SequenceId,
        session.SequenceName,
        new ExecutionLogContext { Origin = ExecutionOrigins.StepThrough },
        CancellationToken.None).ConfigureAwait(false);
    }
    catch (Exception ex) {
      if (_logger is not null) StepThroughLog.LogWriteFailed(_logger, session.Id, ex);
      return string.Empty;
    }
  }

  private async Task FinishLogAsync(StepThroughSession session, string rootId, List<HistoryEntry> entries) {
    if (string.IsNullOrEmpty(rootId)) return;
    try {
      var failed = entries.Any(e => e.Status is "Failed" or "Cancelled");
      var details = new List<ExecutionDetailItem>();
      var order = 1;
      foreach (var entry in entries) {
        details.Add(ToDetail(session, entry, order++));
      }

      var last = entries.Count > 0 ? entries[^1] : null;
      var summary = last is null
        ? $"Step-through of sequence '{session.SequenceName}': no step ran."
        : $"Step-through of sequence '{session.SequenceName}': step '{LabelOf(session, last)}' {last.Status.ToLowerInvariant()}.";
      await _log.LogSequenceFinalizeAsync(
        rootId,
        session.SequenceId,
        session.SequenceName,
        failed ? "failure" : "success",
        summary,
        new ExecutionLogContext {
          Depth = 0,
          SequenceId = session.SequenceId,
          SequenceLabel = session.SequenceName,
          Origin = ExecutionOrigins.StepThrough
        },
        details,
        CancellationToken.None).ConfigureAwait(false);

      lock (session.Gate) {
        foreach (var entry in entries) session.State.SetExecutionLogId(entry.Seq, rootId);
      }
    }
    catch (Exception ex) {
      if (_logger is not null) StepThroughLog.LogWriteFailed(_logger, session.Id, ex);
    }
  }

  private static string LabelOf(StepThroughSession session, HistoryEntry entry)
    => session.Nodes.FirstOrDefault(n => string.Equals(n.Path, entry.Path, StringComparison.Ordinal))?.Label
       ?? entry.StepId
       ?? entry.Path;

  private static ExecutionDetailItem ToDetail(StepThroughSession session, HistoryEntry entry, int order) {
    var node = session.Nodes.FirstOrDefault(n => string.Equals(n.Path, entry.Path, StringComparison.Ordinal));
    var stepType = entry.Kind switch {
      HistoryKind.Enter => node?.Type == "if" ? "if" : "loop",
      HistoryKind.Exit => "loop",
      _ => node?.Type == "wait-for-image" ? "waitForImage" : "command"
    };
    var outcome = entry.Outcome ?? entry.Status.ToLowerInvariant();
    var label = LabelOf(session, entry);
    var text = string.IsNullOrWhiteSpace(entry.Message)
      ? $"Step '{label}' {outcome}."
      : $"Step '{label}' {outcome}: {entry.Message}";
    return new ExecutionDetailItem(
      "step",
      text,
      new Dictionary<string, object?> {
        ["stepOrder"] = order,
        ["stepType"] = stepType,
        ["status"] = entry.Status,
        ["actionOutcome"] = outcome,
        ["reasonCode"] = outcome,
        ["message"] = entry.Message,
        ["sequenceId"] = session.SequenceId,
        ["sequenceLabel"] = session.SequenceName,
        ["stepId"] = entry.StepId ?? entry.Path,
        ["stepLabel"] = label,
        ["path"] = entry.Path,
        ["iteration"] = entry.Iteration,
        ["effects"] = entry.Effects.Count == 0 ? null : string.Join("; ", entry.Effects),
        ["notes"] = entry.Notes.Count == 0 ? null : string.Join("; ", entry.Notes),
        ["origin"] = ExecutionOrigins.StepThrough,
        ["durationMs"] = entry.DurationMs.ToString(CultureInfo.InvariantCulture)
      },
      "normal");
  }
}

internal static partial class StepThroughLog {
  [LoggerMessage(EventId = 1170, Level = LogLevel.Warning, Message = "Step-through {SessionId} could not run a step")]
  public static partial void StepFailed(ILogger logger, string SessionId, Exception ex);

  [LoggerMessage(EventId = 1171, Level = LogLevel.Warning, Message = "Step-through {SessionId} could not write its execution log entry")]
  public static partial void LogWriteFailed(ILogger logger, string SessionId, Exception ex);

  [LoggerMessage(EventId = 1172, Level = LogLevel.Information, Message = "Step-through {SessionId} ended: its lease expired")]
  public static partial void LeaseExpired(ILogger logger, string SessionId);
}
