using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GameBot.Domain.Commands;
using GameBot.Domain.Queues;
using GameBot.Service.Contracts.Queues;
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

      var created = await repo.CreateAsync(new ExecutionQueue {
        Name = name,
        EmulatorSerial = serial,
        CycleExecution = req!.CycleExecution
      }).ConfigureAwait(false);
      return Results.Created($"{ApiRoutes.Queues}/{created.Id}", BuildResponse(created, runtime));
    }).WithName("CreateQueue");

    group.MapGet("", async (IQueueRepository repo, IQueueRuntimeStore runtime) => {
      var list = await repo.ListAsync().ConfigureAwait(false);
      var resp = list.Select(q => BuildResponse(q, runtime)).ToList();
      return Results.Ok(resp);
    }).WithName("ListQueues");

    group.MapGet("{id}", async (string id, IQueueRepository repo, IQueueRuntimeStore runtime, ISequenceRepository sequences) => {
      var queue = await repo.GetAsync(id).ConfigureAwait(false);
      if (queue is null) return NotFound();
      return Results.Ok(await BuildDetailAsync(queue, runtime, sequences).ConfigureAwait(false));
    }).WithName("GetQueue");

    group.MapPut("{id}", async (string id, UpdateQueueRequest? req, IQueueRepository repo, IQueueRuntimeStore runtime) => {
      var queue = await repo.GetAsync(id).ConfigureAwait(false);
      if (queue is null) return NotFound();
      if (runtime.GetStatus(id) == QueueExecutionStatus.Running)
        return Error(409, "queue_running", "Stop the queue before editing.");
      var name = req?.Name?.Trim();
      if (string.IsNullOrWhiteSpace(name)) return Error(400, "invalid_request", "name is required");
      queue.Name = name;
      queue.CycleExecution = req!.CycleExecution;
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

    group.MapPut("{id}/entries", async (string id, ReplaceQueueEntriesRequest? req, IQueueRepository repo, IQueueRuntimeStore runtime, ISequenceRepository sequences) => {
      var queue = await repo.GetAsync(id).ConfigureAwait(false);
      if (queue is null) return NotFound();
      if (runtime.GetStatus(id) == QueueExecutionStatus.Running)
        return Error(409, "queue_running", "Stop the queue before loading a template.");
      runtime.SetEntries(id, req?.SequenceIds ?? Array.Empty<string>());
      return Results.Ok(await BuildDetailAsync(queue, runtime, sequences).ConfigureAwait(false));
    }).WithName("ReplaceQueueEntries");

    group.MapDelete("{id}/entries/{entryId}", async (string id, string entryId, IQueueRepository repo, IQueueRuntimeStore runtime) => {
      var queue = await repo.GetAsync(id).ConfigureAwait(false);
      if (queue is null) return NotFound();
      return runtime.RemoveEntry(id, entryId)
        ? Results.NoContent()
        : Error(404, "not_found", "Queue entry not found");
    }).WithName("RemoveQueueEntry");

    group.MapPost("{id}/start", async (string id, IQueueRepository repo, IQueueRuntimeStore runtime, ILoggerFactory loggerFactory) => {
      var queue = await repo.GetAsync(id).ConfigureAwait(false);
      if (queue is null) return NotFound();
      runtime.SetStatus(id, QueueExecutionStatus.Running);
      loggerFactory.CreateLogger("Queues").LogQueueStarted(id, queue.EmulatorSerial);
      return Results.Ok(BuildResponse(queue, runtime));
    }).WithName("StartQueue");

    group.MapPost("{id}/stop", async (string id, IQueueRepository repo, IQueueRuntimeStore runtime, ILoggerFactory loggerFactory) => {
      var queue = await repo.GetAsync(id).ConfigureAwait(false);
      if (queue is null) return NotFound();
      runtime.SetStatus(id, QueueExecutionStatus.Stopped);
      loggerFactory.CreateLogger("Queues").LogQueueStopped(id);
      return Results.Ok(BuildResponse(queue, runtime));
    }).WithName("StopQueue");

    return app;
  }

  private static QueueResponse BuildResponse(ExecutionQueue queue, IQueueRuntimeStore runtime) => new() {
    Id = queue.Id,
    Name = queue.Name,
    EmulatorSerial = queue.EmulatorSerial,
    CycleExecution = queue.CycleExecution,
    Status = runtime.GetStatus(queue.Id),
    EntryCount = runtime.GetEntries(queue.Id).Count
  };

  private static async Task<QueueDetailResponse> BuildDetailAsync(ExecutionQueue queue, IQueueRuntimeStore runtime, ISequenceRepository sequences) {
    var entries = runtime.GetEntries(queue.Id);
    var allSequences = await sequences.ListAsync().ConfigureAwait(false);
    var namesById = allSequences.ToDictionary(s => s.Id, s => s.Name, StringComparer.Ordinal);
    var detail = new QueueDetailResponse {
      Id = queue.Id,
      Name = queue.Name,
      EmulatorSerial = queue.EmulatorSerial,
      CycleExecution = queue.CycleExecution,
      Status = runtime.GetStatus(queue.Id),
      EntryCount = entries.Count
    };
    foreach (var entry in entries) {
      var found = namesById.TryGetValue(entry.SequenceId, out var name);
      detail.Entries.Add(ProjectEntry(entry, found ? name : null));
    }
    return detail;
  }

  private static QueueEntryResponse ProjectEntry(QueueEntry entry, string? sequenceName) => new() {
    EntryId = entry.EntryId,
    SequenceId = entry.SequenceId,
    SequenceName = sequenceName,
    Stale = sequenceName is null
  };

  private static IResult NotFound() => Error(404, "not_found", "Queue not found");

  private static IResult Error(int status, string code, string message) =>
    Results.Json(new { error = new { code, message, hint = (string?)null } }, statusCode: status);
}
