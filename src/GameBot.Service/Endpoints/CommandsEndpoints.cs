using GameBot.Domain.Commands;
using GameBot.Service;
using GameBot.Service.Models;
using GameBot.Service.Services;
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

  private static CommandStepType MapStepTypeFromDto(CommandStepTypeDto dto) => dto switch {
    CommandStepTypeDto.Command => CommandStepType.Command,
    CommandStepTypeDto.WaitForImage => CommandStepType.WaitForImage,
    _ => CommandStepType.PrimitiveTap
  };

  private static CommandStepTypeDto MapStepTypeToDto(CommandStepType type) => type switch {
    CommandStepType.Command => CommandStepTypeDto.Command,
    CommandStepType.WaitForImage => CommandStepTypeDto.WaitForImage,
    _ => CommandStepTypeDto.PrimitiveTap
  };

  private static PrimitiveTapConfig? ToDomainPrimitiveTap(PrimitiveTapConfigDto? dto) {
    if (dto is null) return null;
    var detection = ToDomainDetection(dto.DetectionTarget);
    if (detection is null) return null;
    return new PrimitiveTapConfig { DetectionTarget = detection };
  }

  private static PrimitiveTapConfigDto? ToResponsePrimitiveTap(PrimitiveTapConfig? cfg) {
    if (cfg is null) return null;
    return new PrimitiveTapConfigDto { DetectionTarget = ToResponseDetection(cfg.DetectionTarget)! };
  }

  private static WaitForImageConfig? ToDomainWaitForImage(WaitForImageConfigDto? dto) {
    if (dto is null) return null;
    var timeoutMs = dto.TimeoutMs ?? 1000;
    if (timeoutMs < 0) {
      timeoutMs = 1000;
    }

    return new WaitForImageConfig {
      DetectionTarget = ToDomainDetection(dto.DetectionTarget),
      TimeoutMs = timeoutMs
    };
  }

  private static WaitForImageConfigDto? ToResponseWaitForImage(WaitForImageConfig? cfg) {
    if (cfg is null) return null;
    return new WaitForImageConfigDto {
      DetectionTarget = ToResponseDetection(cfg.DetectionTarget),
      TimeoutMs = cfg.TimeoutMs
    };
  }

  private static string? ValidateStep(CommandStepDto step) {
    if (step.Type == CommandStepTypeDto.PrimitiveTap) {
      if (step.PrimitiveTap is null || step.PrimitiveTap.DetectionTarget is null || string.IsNullOrWhiteSpace(step.PrimitiveTap.DetectionTarget.ReferenceImageId)) {
        return "primitiveTap.detectionTarget.referenceImageId is required for PrimitiveTap steps";
      }
      return null;
    }

    if (step.Type == CommandStepTypeDto.WaitForImage) {
      if (step.WaitForImage is null) {
        return "waitForImage is required for WaitForImage steps";
      }

      if (step.WaitForImage.TimeoutMs is < 0) {
        return "waitForImage.timeoutMs must be greater than or equal to zero";
      }

      if (step.WaitForImage.DetectionTarget is not null
          && string.IsNullOrWhiteSpace(step.WaitForImage.DetectionTarget.ReferenceImageId)) {
        return "waitForImage.detectionTarget.referenceImageId must not be empty when detectionTarget is provided";
      }

      return null;
    }

    if (string.IsNullOrWhiteSpace(step.TargetId)) {
      return "targetId is required for Command steps";
    }
    return null;
  }

  private static CommandStep ToDomainStep(CommandStepDto s) => new() {
    Type = MapStepTypeFromDto(s.Type),
    TargetId = s.TargetId ?? string.Empty,
    PrimitiveTap = s.Type == CommandStepTypeDto.PrimitiveTap ? ToDomainPrimitiveTap(s.PrimitiveTap) : null,
    WaitForImage = s.Type == CommandStepTypeDto.WaitForImage ? ToDomainWaitForImage(s.WaitForImage) : null,
    Order = s.Order
  };

  private static CommandStepDto ToResponseStep(CommandStep s) => new() {
    Type = MapStepTypeToDto(s.Type),
    TargetId = s.Type == CommandStepType.Command ? s.TargetId : null,
    PrimitiveTap = s.Type == CommandStepType.PrimitiveTap ? ToResponsePrimitiveTap(s.PrimitiveTap) : null,
    WaitForImage = s.Type == CommandStepType.WaitForImage ? ToResponseWaitForImage(s.WaitForImage) : null,
    Order = s.Order
  };

  private static StepExecutionOutcomeDto ToResponseOutcome(PrimitiveTapStepOutcome outcome) => new() {
    StepOrder = outcome.StepOrder,
    Status = outcome.Status,
    StepType = outcome.StepType ?? "primitiveTap",
    Reason = outcome.Reason,
    DetectionConfidence = outcome.DetectionConfidence,
    TimeoutMs = outcome.TimeoutMs,
    EffectiveTimeoutMs = outcome.EffectiveTimeoutMs,
    ReferenceImageId = outcome.ReferenceImageId,
    ImageLoadStatus = outcome.ImageLoadStatus,
    ResolvedPoint = outcome.ResolvedPoint is null
      ? null
      : new ResolvedPointDto {
        X = outcome.ResolvedPoint.X,
        Y = outcome.ResolvedPoint.Y
      }
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
      var req = root.Deserialize<CreateCommandRequest>(WebJsonOptions);
      if (req is null || string.IsNullOrWhiteSpace(req.Name))
        return Results.BadRequest(new { error = new { code = "invalid_request", message = "name is required", hint = (string?)null } });

      foreach (var s in req.Steps) {
        var err = ValidateStep(s);
        if (err is not null) {
          return Results.BadRequest(new { error = new { code = "invalid_request", message = err, hint = (string?)null } });
        }
      }

      var detectionDto = req.Detection ?? TryReadDetection(root);

      var command = new Command {
        Id = string.Empty,
        Name = req.Name,
        TriggerId = req.TriggerId,
        Steps = new Collection<CommandStep>(req.Steps.Select(ToDomainStep).ToList()),
        Detection = ToDomainDetection(detectionDto)
      };

      var createdDomain = await repo.AddAsync(command, ct).ConfigureAwait(false);
      return Results.Created($"{ApiRoutes.Commands}/{createdDomain.Id}", new CommandResponse {
        Id = createdDomain.Id,
        Name = createdDomain.Name,
        TriggerId = createdDomain.TriggerId,
        Steps = new Collection<CommandStepDto>(createdDomain.Steps.Select(ToResponseStep).ToList()),
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
            Steps = new Collection<CommandStepDto>(c.Steps.Select(ToResponseStep).ToList()),
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
        Steps = new Collection<CommandStepDto>(c.Steps.Select(ToResponseStep).ToList()),
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
        foreach (var s in req.Steps) {
          var err = ValidateStep(s);
          if (err is not null) {
            return Results.BadRequest(new { error = new { code = "invalid_request", message = err, hint = (string?)null } });
          }
        }
        existing.Steps.Clear();
        foreach (var s in req.Steps) {
          existing.Steps.Add(ToDomainStep(s));
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
        Steps = new Collection<CommandStepDto>(updated.Steps.Select(ToResponseStep).ToList()),
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
        var result = await exec.ForceExecuteDetailedAsync(sessionId, id, ct).ConfigureAwait(false);
        return Results.Accepted($"{ApiRoutes.Sessions}/{sessionId}", new {
          accepted = result.Accepted,
          stepOutcomes = result.StepOutcomes.Select(ToResponseOutcome).ToArray()
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
      catch (InvalidOperationException ex) when (string.Equals(ex.Message, "command_has_no_steps", StringComparison.OrdinalIgnoreCase)) {
        return Results.BadRequest(new { error = new { code = "invalid_command", message = "Command has no executable steps.", hint = "Edit the command and add at least one step (for example, PrimitiveTap)." } });
      }
    })
    .WithName("ForceExecuteCommand")
    .WithTags("Commands");

    app.MapPost($"{ApiRoutes.Commands}/{{id}}/evaluate-and-execute", async (string id, string? sessionId, GameBot.Service.Services.ICommandExecutor exec, CancellationToken ct) => {
      try {
        var decision = await exec.EvaluateAndExecuteDetailedAsync(sessionId, id, ct).ConfigureAwait(false);
        return Results.Accepted($"{ApiRoutes.Sessions}/{sessionId}", new {
          accepted = decision.Accepted,
          triggerStatus = decision.TriggerStatus.ToString(),
          message = decision.Reason,
          stepOutcomes = decision.StepOutcomes.Select(ToResponseOutcome).ToArray()
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
      catch (InvalidOperationException ex) when (string.Equals(ex.Message, "command_has_no_steps", StringComparison.OrdinalIgnoreCase)) {
        return Results.BadRequest(new { error = new { code = "invalid_command", message = "Command has no executable steps.", hint = "Edit the command and add at least one step (for example, PrimitiveTap)." } });
      }
    })
    .WithName("EvaluateAndExecuteCommand")
    .WithTags("Commands");

    return app;
  }
}
