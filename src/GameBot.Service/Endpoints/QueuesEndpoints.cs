using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GameBot.Domain.Commands;
using GameBot.Domain.Games;
using GameBot.Domain.Queues;
using GameBot.Domain.QueueTemplates;
using GameBot.Service.Contracts.Queues;
using GameBot.Service.Services.QueueExecution;
using Microsoft.Extensions.Logging;

namespace GameBot.Service.Endpoints;

internal static class QueuesEndpoints {
  public static IEndpointRouteBuilder MapQueueEndpoints(this IEndpointRouteBuilder app) {
    var group = app.MapGroup(ApiRoutes.Queues).WithTags("Queues");

    group.MapPost("", async (CreateQueueRequest? req, IQueueRepository repo, IQueueRuntimeStore runtime) => {
      var name = req?.Name?.Trim();
      if (string.IsNullOrWhiteSpace(name)) return Error(400, "invalid_request", "name is required");
      var serial = req?.EmulatorSerial?.Trim();
      if (string.IsNullOrWhiteSpace(serial)) return Error(400, "invalid_request", "emulatorSerial is required");
      if (req!.EmulatorInstanceIndex is < 0) return Error(400, "invalid_request", "emulatorInstanceIndex must be >= 0");

      var created = await repo.CreateAsync(new ExecutionQueue {
        Name = name,
        EmulatorSerial = serial,
        CycleExecution = req.CycleExecution,
        PauseWhenIdle = req.PauseWhenIdle,
        IdleThresholdSeconds = CoerceThreshold(req.IdleThresholdSeconds),
        EmulatorInstanceName = NormalizeInstanceName(req.EmulatorInstanceName),
        EmulatorInstanceIndex = req.EmulatorInstanceIndex
      }).ConfigureAwait(false);
      return Results.Created($"{ApiRoutes.Queues}/{created.Id}", BuildResponse(created, runtime));
    }).WithName("CreateQueue");

    group.MapGet("", async (IQueueRepository repo, IQueueRuntimeStore runtime) => {
      var list = await repo.ListAsync().ConfigureAwait(false);
      var resp = list.Select(q => BuildResponse(q, runtime)).ToList();
      return Results.Ok(resp);
    }).WithName("ListQueues");

    group.MapGet("{id}", async (string id, IQueueRepository repo, IQueueRuntimeStore runtime, ISequenceRepository sequences, IQueueTemplateRepository templates, IGameRepository games) => {
      var queue = await repo.GetAsync(id).ConfigureAwait(false);
      if (queue is null) return NotFound();
      await MaybeAutoLoadAsync(queue, repo, runtime, templates).ConfigureAwait(false);
      return Results.Ok(await BuildDetailAsync(queue, runtime, sequences, templates, games).ConfigureAwait(false));
    }).WithName("GetQueue");

    // Live monitor (feature 072): read-only snapshot of what a running queue is doing now and next.
    // Returns 200 with running:false (not 404/409) when the queue exists but is not running, so the
    // client can render the "not running / run ended" state and the last outcome. Safe to poll.
    group.MapGet("{id}/monitor", async (string id, IQueueRepository repo, IQueueMonitorService monitor) => {
      var queue = await repo.GetAsync(id).ConfigureAwait(false);
      if (queue is null) return NotFound();
      var snapshot = await monitor.BuildAsync(id).ConfigureAwait(false);
      return Results.Ok(ProjectMonitor(snapshot));
    }).WithName("GetQueueMonitor");

    group.MapPut("{id}", async (string id, UpdateQueueRequest? req, IQueueRepository repo, IQueueRuntimeStore runtime) => {
      var queue = await repo.GetAsync(id).ConfigureAwait(false);
      if (queue is null) return NotFound();
      if (runtime.GetStatus(id) == QueueExecutionStatus.Running)
        return Error(409, "queue_running", "Stop the queue before editing.");
      var name = req?.Name?.Trim();
      if (string.IsNullOrWhiteSpace(name)) return Error(400, "invalid_request", "name is required");
      if (req!.EmulatorInstanceIndex is < 0) return Error(400, "invalid_request", "emulatorInstanceIndex must be >= 0");
      queue.Name = name;
      queue.CycleExecution = req.CycleExecution;
      queue.PauseWhenIdle = req.PauseWhenIdle;
      queue.IdleThresholdSeconds = CoerceThreshold(req.IdleThresholdSeconds);
      queue.EmulatorInstanceName = NormalizeInstanceName(req.EmulatorInstanceName);
      queue.EmulatorInstanceIndex = req.EmulatorInstanceIndex;
      var saved = await repo.UpdateAsync(queue).ConfigureAwait(false);
      return Results.Ok(BuildResponse(saved, runtime));
    }).WithName("UpdateQueue");

    group.MapDelete("{id}", async (string id, IQueueRepository repo, IQueueRuntimeStore runtime) => {
      var queue = await repo.GetAsync(id).ConfigureAwait(false);
      if (queue is null) return NotFound();
      if (runtime.GetStatus(id) == QueueExecutionStatus.Running)
        return Error(409, "queue_running", "Stop the queue before deleting.");
      await repo.DeleteAsync(id).ConfigureAwait(false);
      runtime.Remove(id);
      return Results.NoContent();
    }).WithName("DeleteQueue");

    group.MapPost("{id}/entries", async (string id, AddQueueEntryRequest? req, IQueueRepository repo, IQueueRuntimeStore runtime, ISequenceRepository sequences) => {
      var queue = await repo.GetAsync(id).ConfigureAwait(false);
      if (queue is null) return NotFound();
      var sequenceId = req?.SequenceId?.Trim();
      if (string.IsNullOrWhiteSpace(sequenceId)) return Error(400, "invalid_request", "sequenceId is required");
      var entry = runtime.AddEntry(id, sequenceId);
      var resolved = await sequences.GetAsync(sequenceId).ConfigureAwait(false);
      return Results.Created($"{ApiRoutes.Queues}/{id}/entries/{entry.EntryId}", ProjectEntry(entry, resolved?.Name));
    }).WithName("AddQueueEntry");

    group.MapPut("{id}/entries", async (string id, ReplaceQueueEntriesRequest? req, IQueueRepository repo, IQueueRuntimeStore runtime, ISequenceRepository sequences, IQueueTemplateRepository templates, IGameRepository games) => {
      var queue = await repo.GetAsync(id).ConfigureAwait(false);
      if (queue is null) return NotFound();
      if (runtime.GetStatus(id) == QueueExecutionStatus.Running)
        return Error(409, "queue_running", "Stop the queue before loading a template.");
      runtime.SetEntries(id, req?.SequenceIds ?? Array.Empty<string>());
      return Results.Ok(await BuildDetailAsync(queue, runtime, sequences, templates, games).ConfigureAwait(false));
    }).WithName("ReplaceQueueEntries");

    group.MapPut("{id}/template", async (string id, SetQueueTemplateLinkRequest? req, IQueueRepository repo, IQueueRuntimeStore runtime, ISequenceRepository sequences, IQueueTemplateRepository templates, IGameRepository games) => {
      var queue = await repo.GetAsync(id).ConfigureAwait(false);
      if (queue is null) return NotFound();
      var templateId = req?.TemplateId;
      if (!string.IsNullOrEmpty(templateId) && await templates.GetAsync(templateId).ConfigureAwait(false) is null)
        return Error(400, "invalid_request", "template not found");
      queue.LinkedTemplateId = string.IsNullOrEmpty(templateId) ? null : templateId;
      var saved = await repo.UpdateAsync(queue).ConfigureAwait(false);
      return Results.Ok(await BuildDetailAsync(saved, runtime, sequences, templates, games).ConfigureAwait(false));
    }).WithName("SetQueueTemplateLink");

    group.MapPut("{id}/game", async (string id, SetQueueGameLinkRequest? req, IQueueRepository repo, IQueueRuntimeStore runtime, ISequenceRepository sequences, IQueueTemplateRepository templates, IGameRepository games) => {
      var queue = await repo.GetAsync(id).ConfigureAwait(false);
      if (queue is null) return NotFound();
      var gameId = req?.GameId;
      if (!string.IsNullOrEmpty(gameId) && await games.GetAsync(gameId).ConfigureAwait(false) is null)
        return Error(400, "invalid_request", "game not found");
      queue.LinkedGameId = string.IsNullOrEmpty(gameId) ? null : gameId;
      var saved = await repo.UpdateAsync(queue).ConfigureAwait(false);
      return Results.Ok(await BuildDetailAsync(saved, runtime, sequences, templates, games).ConfigureAwait(false));
    }).WithName("SetQueueGameLink");

    group.MapDelete("{id}/entries/{entryId}", async (string id, string entryId, IQueueRepository repo, IQueueRuntimeStore runtime) => {
      var queue = await repo.GetAsync(id).ConfigureAwait(false);
      if (queue is null) return NotFound();
      return runtime.RemoveEntry(id, entryId)
        ? Results.NoContent()
        : Error(404, "not_found", "Queue entry not found");
    }).WithName("RemoveQueueEntry");

    group.MapPost("{id}/start", async (
        string id,
        IQueueRepository repo,
        IQueueRuntimeStore runtime,
        IQueueExecutionService execution,
        IQueueTemplateRepository templates,
        ISequenceRepository sequences,
        ICommandRepository commands,
        IDeviceClaimRegistry deviceClaims,
        ILoggerFactory loggerFactory) => {
      // Feature 078 (FR-022): refuse the start BEFORE any session or device work when an enabled
      // entry has a required parameter nothing in scope can supply. Failing here beats discovering it
      // at 03:00 halfway through a run.
      var preflightQueue = await repo.GetAsync(id).ConfigureAwait(false);
      if (preflightQueue is null) return NotFound();
      var unsatisfied = await FindUnsatisfiedRequiredParametersAsync(
          preflightQueue, templates, sequences, commands).ConfigureAwait(false);
      if (unsatisfied.Count > 0) {
        return Results.Json(new {
          error = "missing_required_parameters",
          message = "One or more enabled entries have required parameters that nothing can supply.",
          entries = unsatisfied
        }, statusCode: 409);
      }

      var outcome = await execution.StartAsync(id).ConfigureAwait(false);
      if (outcome == QueueStartOutcome.NotFound) return NotFound();
      if (outcome == QueueStartOutcome.AlreadyRunning) return Error(409, "already_running", "The queue is already running.");
      if (outcome == QueueStartOutcome.DeviceInUse) {
        // Feature 079 (FR-009/FR-010): a different queue holds this emulator. Name the device and the
        // holder so the operator can act, and keep this distinct from "already_running".
        var serial = preflightQueue.EmulatorSerial;
        var holderId = deviceClaims.TryGetHolder(serial, out var holder) ? holder.QueueId : "another queue";
        var holderName = holder is null || string.IsNullOrWhiteSpace(holder.QueueName) ? holderId : holder.QueueName;
        loggerFactory.CreateLogger("Queues").LogQueueStartRefusedDeviceInUse(id, serial, holderId, holderName);
        return Error(409, "device_in_use",
          $"Emulator '{serial}' is already in use by queue '{holderName}'. Stop that queue before starting this one.");
      }
      var queue = await repo.GetAsync(id).ConfigureAwait(false);
      if (queue is null) return NotFound();
      loggerFactory.CreateLogger("Queues").LogQueueStarted(id, queue.EmulatorSerial);
      return Results.Ok(BuildResponse(queue, runtime));
    }).WithName("StartQueue");

    group.MapPost("{id}/stop", async (string id, IQueueRepository repo, IQueueRuntimeStore runtime, IQueueExecutionService execution, ILoggerFactory loggerFactory) => {
      var queue = await repo.GetAsync(id).ConfigureAwait(false);
      if (queue is null) return NotFound();
      await execution.StopAsync(id).ConfigureAwait(false);
      loggerFactory.CreateLogger("Queues").LogQueueStopped(id);
      return Results.Ok(BuildResponse(queue, runtime));
    }).WithName("StopQueue");

    // Live relative scheduling (feature 059): schedule any library sequence to fire once after a
    // relative offset from now against the queue's active run. Ephemeral; never persisted.
    group.MapPost("{id}/live-schedule", async (string id, LiveScheduleRequest? req, IQueueRepository repo, ISequenceRepository sequences, IQueueExecutionService execution) => {
      // (1) Queue must exist.
      var queue = await repo.GetAsync(id).ConfigureAwait(false);
      if (queue is null) return NotFound();

      // (2) Offset must be a well-formed, non-negative, in-range HH:mm:ss duration.
      if (!RelativeOffsetParser.TryParse(req?.Offset, out var offset, out var offsetError))
        return Error(400, "invalid_request", offsetError!);

      // (3) Target sequence must exist in the library.
      var sequenceId = req?.SequenceId?.Trim();
      if (string.IsNullOrWhiteSpace(sequenceId))
        return Error(400, "invalid_request", "sequenceId is required");
      if (await sequences.GetAsync(sequenceId).ConfigureAwait(false) is null)
        return Error(404, "not_found", "Sequence not found");

      // (4) The queue must have an active run to schedule against.
      var result = execution.ScheduleRelative(id, sequenceId, offset);
      if (result.Outcome == LiveScheduleOutcome.NotRunning)
        return Error(409, "not_running", "The queue has no active run to schedule against.");

      return Results.Ok(new LiveScheduleResponse {
        SequenceId = sequenceId,
        Offset = RelativeOffsetParser.Format(offset),
        ExpectedFireAt = result.ExpectedFireAt
      });
    }).WithName("LiveScheduleQueueSequence");

    return app;
  }

  /// <summary>
  /// Auto-loads the linked template's entries into the queue's runtime on the first display
  /// after a service start. Skips when unlinked, running, or already materialized; clears and
  /// persists a now-unresolvable link instead of erroring (FR-006/010/011/012).
  /// </summary>
  private static async Task MaybeAutoLoadAsync(ExecutionQueue queue, IQueueRepository repo, IQueueRuntimeStore runtime, IQueueTemplateRepository templates) {
    if (string.IsNullOrEmpty(queue.LinkedTemplateId)) return;
    if (runtime.GetStatus(queue.Id) == QueueExecutionStatus.Running) return;
    if (runtime.HasRuntimeState(queue.Id)) return;
    var template = await templates.GetAsync(queue.LinkedTemplateId).ConfigureAwait(false);
    if (template is null) {
      queue.LinkedTemplateId = null;
      await repo.UpdateAsync(queue).ConfigureAwait(false);
      return;
    }
    runtime.SetEntries(queue.Id, template.Entries.Select(e => e.SequenceId));
  }

  /// <summary>
  /// Finds, per enabled template entry, the required parameters that neither the entry, the queue's
  /// built-ins, nor a declared default can supply (feature 078, FR-022). An unlinked template or a
  /// stale sequence reference contributes nothing — those are reported by their own existing checks.
  /// </summary>
  private static async Task<List<object>> FindUnsatisfiedRequiredParametersAsync(
      ExecutionQueue queue,
      IQueueTemplateRepository templates,
      ISequenceRepository sequences,
      ICommandRepository commands) {
    var problems = new List<object>();
    if (string.IsNullOrWhiteSpace(queue.LinkedTemplateId)) return problems;

    var template = await templates.GetAsync(queue.LinkedTemplateId).ConfigureAwait(false);
    if (template is null) return problems;

    var queueSuppliedNames = GameBot.Domain.Parameters.ParameterScope.FromQueue(queue)
        .Describe()
        .Where(e => e.Value is not null)
        .Select(e => e.Name)
        .ToHashSet(StringComparer.Ordinal);

    for (var index = 0; index < template.Entries.Count; index++) {
      var entry = template.Entries[index];
      if (!entry.Enabled) continue;

      var sequence = await sequences.GetAsync(entry.SequenceId).ConfigureAwait(false);
      if (sequence is null) continue;

      var reachable = await CollectReachableDeclarationsAsync(sequence, commands).ConfigureAwait(false);
      var missing = GameBot.Domain.Services.ParameterValidationService.FindUnsatisfiedRequired(
          entry, sequence, reachable, queueSuppliedNames);
      if (missing.Count == 0) continue;

      problems.Add(new {
        entryIndex = index,
        sequenceId = entry.SequenceId,
        sequenceName = sequence.Name,
        missing
      });
    }

    return problems;
  }

  /// <summary>
  /// Declarations of every command reachable from a sequence, following command steps and nested
  /// command steps. Cycles are guarded by a visited set.
  /// </summary>
  internal static async Task<List<GameBot.Domain.Parameters.ParameterDeclaration>> CollectReachableDeclarationsAsync(
      CommandSequence sequence,
      ICommandRepository commands) {
    var declarations = new List<GameBot.Domain.Parameters.ParameterDeclaration>();
    var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    var pending = new Queue<string>();

    foreach (var step in FlattenSequenceSteps(sequence.Steps)) {
      if (!string.IsNullOrWhiteSpace(step.CommandId)) pending.Enqueue(step.CommandId);
    }

    while (pending.Count > 0) {
      var commandId = pending.Dequeue();
      if (!visited.Add(commandId)) continue;
      var command = await commands.GetAsync(commandId).ConfigureAwait(false);
      if (command is null) continue;
      declarations.AddRange(command.Parameters);
      foreach (var step in command.Steps) {
        if (step.Type == CommandStepType.Command && !string.IsNullOrWhiteSpace(step.TargetId)) {
          pending.Enqueue(step.TargetId);
        }
      }
    }

    return declarations;
  }

  private static IEnumerable<SequenceStep> FlattenSequenceSteps(IEnumerable<SequenceStep> steps) {
    foreach (var step in steps) {
      yield return step;
      foreach (var child in FlattenSequenceSteps(step.Body)) yield return child;
      if (step.ElseBody is null) continue;
      foreach (var child in FlattenSequenceSteps(step.ElseBody)) yield return child;
    }
  }

  private static QueueResponse BuildResponse(ExecutionQueue queue, IQueueRuntimeStore runtime) => new() {
    Id = queue.Id,
    Name = queue.Name,
    EmulatorSerial = queue.EmulatorSerial,
    CycleExecution = queue.CycleExecution,
    PauseWhenIdle = queue.PauseWhenIdle,
    IdleThresholdSeconds = queue.IdleThresholdSeconds,
    EmulatorInstanceName = queue.EmulatorInstanceName,
    EmulatorInstanceIndex = queue.EmulatorInstanceIndex,
    Status = runtime.GetStatus(queue.Id),
    EntryCount = runtime.GetEntries(queue.Id).Count,
    LinkedTemplateId = queue.LinkedTemplateId,
    LinkedGameId = queue.LinkedGameId
  };

  private static async Task<QueueDetailResponse> BuildDetailAsync(ExecutionQueue queue, IQueueRuntimeStore runtime, ISequenceRepository sequences, IQueueTemplateRepository templates, IGameRepository games) {
    var entries = runtime.GetEntries(queue.Id);
    var allSequences = await sequences.ListAsync().ConfigureAwait(false);
    var namesById = allSequences.ToDictionary(s => s.Id, s => s.Name, StringComparer.Ordinal);
    var detail = new QueueDetailResponse {
      Id = queue.Id,
      Name = queue.Name,
      EmulatorSerial = queue.EmulatorSerial,
      CycleExecution = queue.CycleExecution,
      PauseWhenIdle = queue.PauseWhenIdle,
      IdleThresholdSeconds = queue.IdleThresholdSeconds,
      EmulatorInstanceName = queue.EmulatorInstanceName,
      EmulatorInstanceIndex = queue.EmulatorInstanceIndex,
      Status = runtime.GetStatus(queue.Id),
      EntryCount = entries.Count,
      LinkedTemplateId = queue.LinkedTemplateId,
      LinkedTemplateName = await ResolveTemplateNameAsync(queue.LinkedTemplateId, templates).ConfigureAwait(false),
      LinkedGameId = queue.LinkedGameId,
      LinkedGameName = await ResolveGameNameAsync(queue.LinkedGameId, games).ConfigureAwait(false)
    };
    foreach (var entry in entries) {
      var found = namesById.TryGetValue(entry.SequenceId, out var name);
      detail.Entries.Add(ProjectEntry(entry, found ? name : null));
    }
    return detail;
  }

  private static async Task<string?> ResolveTemplateNameAsync(string? templateId, IQueueTemplateRepository templates) {
    if (string.IsNullOrEmpty(templateId)) return null;
    var template = await templates.GetAsync(templateId).ConfigureAwait(false);
    return template?.Name;
  }

  private static async Task<string?> ResolveGameNameAsync(string? gameId, IGameRepository games) {
    if (string.IsNullOrEmpty(gameId)) return null;
    var game = await games.GetAsync(gameId).ConfigureAwait(false);
    return game?.Name;
  }

  private static QueueMonitorResponse ProjectMonitor(QueueMonitorSnapshot snapshot) {
    var resp = new QueueMonitorResponse {
      QueueId = snapshot.QueueId,
      Name = snapshot.Name,
      Running = snapshot.Running,
      CycleExecution = snapshot.CycleExecution,
      RunStartedAt = snapshot.RunStartedAt,
      DeviceSerial = snapshot.DeviceSerial,
      Current = snapshot.Current is null ? null : ProjectMonitorItem(snapshot.Current),
      NothingScheduled = snapshot.NothingScheduled,
      LastOutcome = snapshot.LastOutcome is null
        ? null
        : new RunOutcomeResponse { Status = snapshot.LastOutcome.Status, Summary = snapshot.LastOutcome.Summary }
    };
    foreach (var item in snapshot.Upcoming) resp.Upcoming.Add(ProjectMonitorItem(item));
    return resp;
  }

  private static QueueMonitorItemResponse ProjectMonitorItem(QueueMonitorItem item) => new() {
    SequenceId = item.SequenceId,
    SequenceName = item.SequenceName,
    Stale = item.Stale,
    ScheduleKind = item.ScheduleKind.ToString(),
    Reason = item.Reason,
    ExpectedAt = item.ExpectedAt,
    RelativeLabel = item.RelativeLabel,
    Repeats = item.Repeats,
    Order = item.Order
  };

  private static QueueEntryResponse ProjectEntry(QueueEntry entry, string? sequenceName) => new() {
    EntryId = entry.EntryId,
    SequenceId = entry.SequenceId,
    SequenceName = sequenceName,
    Stale = sequenceName is null
  };

  // Idle-detection threshold must be at least 1 second; absent (0) or non-positive coerces to the
  // default 30 (feature 073, FR-010).
  private static int CoerceThreshold(int seconds) => seconds < 1 ? 30 : seconds;

  // Trim the optional emulator instance name to null when blank so an empty string in the request
  // means "unset" (feature 074), matching how the runtime treats a missing identifier.
  private static string? NormalizeInstanceName(string? name) =>
    string.IsNullOrWhiteSpace(name) ? null : name.Trim();

  private static IResult NotFound() => Error(404, "not_found", "Queue not found");

  private static IResult Error(int status, string code, string message) =>
    Results.Json(new { error = new { code, message, hint = (string?)null } }, statusCode: status);
}
