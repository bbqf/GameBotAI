using GameBot.Domain.Commands;
using GameBot.Service;
using GameBot.Service.Models;
using Microsoft.AspNetCore.Mvc;
using System.Collections.ObjectModel;
using System.Text.Json;
using Microsoft.AspNetCore.OpenApi;

namespace GameBot.Service.Endpoints;

internal static class CommandsEndpoints {
  private static readonly JsonSerializerOptions WebJsonOptions = CreateJsonOptions();

  private static DetectionSelectionStrategy MapSelectionFromDto(DetectionSelectionStrategyDto? dto) => dto switch {
    DetectionSelectionStrategyDto.FirstMatch => DetectionSelectionStrategy.FirstMatch,
    _ => DetectionSelectionStrategy.HighestConfidence
  };

  private static DetectionSelectionStrategyDto MapSelectionToDto(DetectionSelectionStrategy selection) => selection switch {
    DetectionSelectionStrategy.FirstMatch => DetectionSelectionStrategyDto.FirstMatch,
    _ => DetectionSelectionStrategyDto.HighestConfidence
  };

  private static DetectionTarget? ToDomainDetection(DetectionTargetDto? dto) {
    if (dto is null) return null;
    if (string.IsNullOrWhiteSpace(dto.ReferenceImageId)) return null;
    var confidence = dto.Confidence ?? 0.8;
    var offsetX = dto.OffsetX ?? 0;
    var offsetY = dto.OffsetY ?? 0;
    var selection = MapSelectionFromDto(dto.SelectionStrategy);
    return new DetectionTarget(dto.ReferenceImageId, confidence, offsetX, offsetY, selection);
  }

  private static DetectionTargetDto? ToResponseDetection(DetectionTarget? detection) {
    if (detection is null) return null;
    return new DetectionTargetDto {
      ReferenceImageId = detection.ReferenceImageId,
      Confidence = detection.Confidence,
      OffsetX = detection.OffsetX,
      OffsetY = detection.OffsetY,
      SelectionStrategy = MapSelectionToDto(detection.SelectionStrategy)
    };
  }

  private static DetectionTargetDto? TryReadDetection(JsonElement root) {
    if (root.ValueKind != JsonValueKind.Object) return null;
    if (root.TryGetProperty("detection", out var detectionProp) && detectionProp.ValueKind == JsonValueKind.Object) {
      return detectionProp.Deserialize<DetectionTargetDto>(WebJsonOptions);
    }
    if (root.TryGetProperty("detectionTarget", out var detectionTargetProp) && detectionTargetProp.ValueKind == JsonValueKind.Object) {
      return detectionTargetProp.Deserialize<DetectionTargetDto>(WebJsonOptions);
    }
    return null;
  }

  private static JsonSerializerOptions CreateJsonOptions() {
    var opts = new JsonSerializerOptions(JsonSerializerDefaults.Web);
    opts.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter());
    return opts;
  }
  public static IEndpointRouteBuilder MapCommandEndpoints(this IEndpointRouteBuilder app) {
    app.MapPost(ApiRoutes.Commands, async (HttpRequest http, ICommandRepository repo, CancellationToken ct) => {
      using var doc = await JsonDocument.ParseAsync(http.Body, cancellationToken: ct).ConfigureAwait(false);
      var root = doc.RootElement;
      // Authoring shape only when explicit actions[] array is provided
      if (root.TryGetProperty("name", out var nameProp) && nameProp.ValueKind == JsonValueKind.String && root.TryGetProperty("actions", out var actionsProp) && actionsProp.ValueKind == JsonValueKind.Array) {
        var name = nameProp.GetString()!.Trim();
        if (string.IsNullOrWhiteSpace(name))
          return Results.BadRequest(new { error = new { code = "invalid_request", message = "name is required", hint = (string?)null } });
        // Authoring shape: actions[] -> steps of type Action
        Collection<CommandStep> steps = new();
        int order = 0;
        foreach (var el in actionsProp.EnumerateArray()) {
          if (el.ValueKind == JsonValueKind.String) {
            steps.Add(new CommandStep { Type = CommandStepType.Action, TargetId = el.GetString()!, Order = order++ });
          }
        }
        var detection = ToDomainDetection(TryReadDetection(root));
        var created = await repo.AddAsync(new Command { Id = string.Empty, Name = name, TriggerId = null, Steps = steps, Detection = detection }, ct).ConfigureAwait(false);
        return Results.Created($"{ApiRoutes.Commands}/{created.Id}", new { id = created.Id, name = created.Name, actions = steps.Select(s => s.TargetId).ToArray(), detection = ToResponseDetection(created.Detection) });
      }

      // Domain shape fallback
      var req = root.Deserialize<CreateCommandRequest>(WebJsonOptions);
      if (req is null || string.IsNullOrWhiteSpace(req.Name))
        return Results.BadRequest(new { error = new { code = "invalid_request", message = "name is required", hint = (string?)null } });

      var detectionDto = req.Detection ?? TryReadDetection(root);

      var command = new Command {
        Id = string.Empty,
        Name = req.Name,
        TriggerId = req.TriggerId,
        Steps = new Collection<CommandStep>(req.Steps.Select(s => new CommandStep {
          Type = s.Type == CommandStepTypeDto.Action ? CommandStepType.Action : CommandStepType.Command,
          TargetId = s.TargetId,
          Order = s.Order
        }).ToList()),
        Detection = ToDomainDetection(detectionDto)
      };

      var createdDomain = await repo.AddAsync(command, ct).ConfigureAwait(false);
      return Results.Created($"{ApiRoutes.Commands}/{createdDomain.Id}", new CommandResponse {
        Id = createdDomain.Id,
        Name = createdDomain.Name,
        TriggerId = createdDomain.TriggerId,
        Steps = new Collection<CommandStepDto>(createdDomain.Steps.Select(s => new CommandStepDto {
          Type = s.Type == CommandStepType.Action ? CommandStepTypeDto.Action : CommandStepTypeDto.Command,
          TargetId = s.TargetId,
          Order = s.Order
        }).ToList()),
        Detection = ToResponseDetection(createdDomain.Detection)
      });
    })
    .WithName("CreateCommand")
    .WithTags("Commands");

    app.MapGet($"{ApiRoutes.Commands}/{{id}}", async (string id, ICommandRepository repo, CancellationToken ct) => {
      var c = await repo.GetAsync(id, ct).ConfigureAwait(false);
      return c is null
          ? Results.NotFound(new { error = new { code = "not_found", message = "Command not found", hint = (string?)null } })
          : Results.Ok(new CommandResponse {
            Id = c.Id,
            Name = c.Name,
            TriggerId = c.TriggerId,
            Steps = new Collection<CommandStepDto>(c.Steps.Select(s => new CommandStepDto {
              Type = s.Type == CommandStepType.Action ? CommandStepTypeDto.Action : CommandStepTypeDto.Command,
              TargetId = s.TargetId,
              Order = s.Order
            }).ToList()),
            Detection = ToResponseDetection(c.Detection)
          });
    })
    .WithName("GetCommand")
    .WithTags("Commands");

    app.MapGet(ApiRoutes.Commands, async (ICommandRepository repo, CancellationToken ct) => {
      var list = await repo.ListAsync(ct).ConfigureAwait(false);
      var resp = list.Select(c => new CommandResponse {
        Id = c.Id,
        Name = c.Name,
        TriggerId = c.TriggerId,
        Steps = new Collection<CommandStepDto>(c.Steps.Select(s => new CommandStepDto {
          Type = s.Type == CommandStepType.Action ? CommandStepTypeDto.Action : CommandStepTypeDto.Command,
          TargetId = s.TargetId,
          Order = s.Order
        }).ToList()),
        Detection = ToResponseDetection(c.Detection)
      });
      return Results.Ok(resp);
    })
    .WithName("ListCommands")
    .WithTags("Commands");

    app.MapPatch($"{ApiRoutes.Commands}/{{id}}", async (string id, UpdateCommandRequest req, ICommandRepository repo, CancellationToken ct) => {
      var existing = await repo.GetAsync(id, ct).ConfigureAwait(false);
      if (existing is null) return Results.NotFound();
      if (!string.IsNullOrWhiteSpace(req.Name)) existing.Name = req.Name!;
      existing.TriggerId = req.TriggerId ?? existing.TriggerId;
      if (req.Steps is not null) {
        existing.Steps.Clear();
        foreach (var s in req.Steps) {
          existing.Steps.Add(new CommandStep {
            Type = s.Type == CommandStepTypeDto.Action ? CommandStepType.Action : CommandStepType.Command,
            TargetId = s.TargetId,
            Order = s.Order
          });
        }
      }
      if (req.DetectionSpecified) {
        existing.Detection = ToDomainDetection(req.Detection);
      }
      var updated = await repo.UpdateAsync(existing, ct).ConfigureAwait(false);
      if (updated is null) return Results.NotFound();
      return Results.Ok(new CommandResponse {
        Id = updated.Id,
        Name = updated.Name,
        TriggerId = updated.TriggerId,
        Steps = new Collection<CommandStepDto>(updated.Steps.Select(s => new CommandStepDto {
          Type = s.Type == CommandStepType.Action ? CommandStepTypeDto.Action : CommandStepTypeDto.Command,
          TargetId = s.TargetId,
          Order = s.Order
        }).ToList()),
        Detection = ToResponseDetection(updated.Detection)
      });
    })
    .WithName("UpdateCommand")
    .WithTags("Commands");

    app.MapDelete($"{ApiRoutes.Commands}/{{id}}", async (string id, ICommandRepository commands, GameBot.Domain.Commands.ISequenceRepository sequences, CancellationToken ct) => {
      var existing = await commands.GetAsync(id, ct).ConfigureAwait(false);
      if (existing is null) return Results.NotFound(new { error = new { code = "not_found", message = "Command not found", hint = (string?)null } });
      // Check references from other commands (steps of type Command)
      var cmdList = await commands.ListAsync(ct).ConfigureAwait(false);
      var referencingCommands = cmdList.Where(c => c.Steps.Any(s => s.Type == CommandStepType.Command && string.Equals(s.TargetId, id, StringComparison.OrdinalIgnoreCase)))
                                       .Select(c => new { id = c.Id, name = c.Name })
                                       .ToArray();
      // Check references from sequences
      var seqList = await sequences.ListAsync().ConfigureAwait(false);
      var referencingSequences = seqList.Where(seq => seq.Steps.Any(s => string.Equals(s.CommandId, id, StringComparison.OrdinalIgnoreCase)))
                                        .Select(seq => new { id = seq.Id, name = seq.Name })
                                        .ToArray();
      if (referencingCommands.Length > 0 || referencingSequences.Length > 0) {
        return Results.Conflict(new { error = new { code = "delete_blocked", message = "Command is referenced.", hint = (string?)null }, references = new { commands = referencingCommands, sequences = referencingSequences } });
      }
      var ok = await commands.DeleteAsync(id, ct).ConfigureAwait(false);
      return ok ? Results.NoContent() : Results.NotFound(new { error = new { code = "not_found", message = "Command not found", hint = (string?)null } });
    })
    .WithName("DeleteCommand")
    .WithTags("Commands");

    app.MapPost($"{ApiRoutes.Commands}/{{id}}/force-execute", async (string id, string? sessionId, GameBot.Service.Services.ICommandExecutor exec, CancellationToken ct) => {
      try {
        var accepted = await exec.ForceExecuteAsync(sessionId, id, ct).ConfigureAwait(false);
        return Results.Accepted($"{ApiRoutes.Sessions}/{sessionId}", new { accepted });
      }
      catch (InvalidOperationException ex) when (string.Equals(ex.Message, "missing_session_context", StringComparison.OrdinalIgnoreCase)) {
        return Results.BadRequest(new { error = new { code = "missing_session", message = "No sessionId supplied and no connect-to-game context found for this command.", hint = "Run a connect-to-game action first or supply sessionId." } });
      }
      catch (KeyNotFoundException ex) when (string.Equals(ex.Message, "cached_session_not_found", StringComparison.OrdinalIgnoreCase)) {
        return Results.BadRequest(new { error = new { code = "missing_session", message = "No cached session found for the command's game/device.", hint = "Run a connect-to-game action first or supply sessionId." } });
      }
      catch (KeyNotFoundException ex) {
        var msg = ex.Message.Contains("Session", StringComparison.OrdinalIgnoreCase) ? "Session not found" : ex.Message;
        return Results.NotFound(new { error = new { code = "not_found", message = msg, hint = (string?)null } });
      }
      catch (InvalidOperationException ex) when (string.Equals(ex.Message, "not_running", StringComparison.OrdinalIgnoreCase)) {
        return Results.Conflict(new { error = new { code = "not_running", message = "Session not running.", hint = (string?)null } });
      }
      catch (InvalidOperationException ex) when (string.Equals(ex.Message, "command_cycle_detected", StringComparison.OrdinalIgnoreCase)) {
        return Results.BadRequest(new { error = new { code = "cycle_detected", message = "Cycle detected in command graph.", hint = (string?)null } });
      }
    })
    .WithName("ForceExecuteCommand")
    .WithTags("Commands");

    app.MapPost($"{ApiRoutes.Commands}/{{id}}/evaluate-and-execute", async (string id, string? sessionId, GameBot.Service.Services.ICommandExecutor exec, CancellationToken ct) => {
      try {
        var decision = await exec.EvaluateAndExecuteAsync(sessionId, id, ct).ConfigureAwait(false);
        return Results.Accepted($"{ApiRoutes.Sessions}/{sessionId}", new {
          accepted = decision.Accepted,
          triggerStatus = decision.TriggerStatus.ToString(),
          message = decision.Reason
        });
      }
      catch (InvalidOperationException ex) when (string.Equals(ex.Message, "missing_session_context", StringComparison.OrdinalIgnoreCase)) {
        return Results.BadRequest(new { error = new { code = "missing_session", message = "No sessionId supplied and no connect-to-game context found for this command.", hint = "Run a connect-to-game action first or supply sessionId." } });
      }
      catch (KeyNotFoundException ex) when (string.Equals(ex.Message, "cached_session_not_found", StringComparison.OrdinalIgnoreCase)) {
        return Results.BadRequest(new { error = new { code = "missing_session", message = "No cached session found for the command's game/device.", hint = "Run a connect-to-game action first or supply sessionId." } });
      }
      catch (KeyNotFoundException ex) {
        var msg = ex.Message.Contains("Session", StringComparison.OrdinalIgnoreCase) ? "Session not found" : ex.Message;
        return Results.NotFound(new { error = new { code = "not_found", message = msg, hint = (string?)null } });
      }
      catch (InvalidOperationException ex) when (string.Equals(ex.Message, "not_running", StringComparison.OrdinalIgnoreCase)) {
        return Results.Conflict(new { error = new { code = "not_running", message = "Session not running.", hint = (string?)null } });
      }
      catch (InvalidOperationException ex) when (string.Equals(ex.Message, "command_cycle_detected", StringComparison.OrdinalIgnoreCase)) {
        return Results.BadRequest(new { error = new { code = "cycle_detected", message = "Cycle detected in command graph.", hint = (string?)null } });
      }
    })
    .WithName("EvaluateAndExecuteCommand")
    .WithTags("Commands");

    return app;
  }
}
