using System.Collections.ObjectModel;
using GameBot.Domain.Commands;
using GameBot.Service.Models;
using Microsoft.AspNetCore.OpenApi;

namespace GameBot.Service.Endpoints;

internal static class CommandsEndpoints {
  public static IEndpointRouteBuilder MapCommandEndpoints(this IEndpointRouteBuilder app) {
    app.MapPost("/commands", async (CreateCommandRequest req, ICommandRepository repo, CancellationToken ct) => {
      if (string.IsNullOrWhiteSpace(req.Name))
        return Results.BadRequest(new { error = new { code = "invalid_request", message = "name is required", hint = (string?)null } });

      var command = new Command {
        Id = string.Empty,
        Name = req.Name,
        TriggerId = req.TriggerId,
        Steps = new Collection<CommandStep>(req.Steps.Select(s => new CommandStep {
          Type = s.Type == CommandStepTypeDto.Action ? CommandStepType.Action : CommandStepType.Command,
          TargetId = s.TargetId,
          Order = s.Order
        }).ToList())
      };

      var created = await repo.AddAsync(command, ct).ConfigureAwait(false);
      return Results.Created($"/commands/{created.Id}", new CommandResponse {
        Id = created.Id,
        Name = created.Name,
        TriggerId = created.TriggerId,
        Steps = new Collection<CommandStepDto>(created.Steps.Select(s => new CommandStepDto {
          Type = s.Type == CommandStepType.Action ? CommandStepTypeDto.Action : CommandStepTypeDto.Command,
          TargetId = s.TargetId,
          Order = s.Order
        }).ToList())
      });
    })
    .WithName("CreateCommand")
    .WithTags("Commands");

    app.MapGet("/commands/{id}", async (string id, ICommandRepository repo, CancellationToken ct) => {
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
            }).ToList())
          });
    })
    .WithName("GetCommand")
    .WithTags("Commands");

    app.MapGet("/commands", async (ICommandRepository repo, CancellationToken ct) => {
      var list = await repo.ListAsync(ct).ConfigureAwait(false);
      var resp = list.Select(c => new CommandResponse {
        Id = c.Id,
        Name = c.Name,
        TriggerId = c.TriggerId,
        Steps = new Collection<CommandStepDto>(c.Steps.Select(s => new CommandStepDto {
          Type = s.Type == CommandStepType.Action ? CommandStepTypeDto.Action : CommandStepTypeDto.Command,
          TargetId = s.TargetId,
          Order = s.Order
        }).ToList())
      });
      return Results.Ok(resp);
    })
    .WithName("ListCommands")
    .WithTags("Commands");

    app.MapPatch("/commands/{id}", async (string id, UpdateCommandRequest req, ICommandRepository repo, CancellationToken ct) => {
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
        }).ToList())
      });
    })
    .WithName("UpdateCommand")
    .WithTags("Commands");

    app.MapDelete("/commands/{id}", async (string id, ICommandRepository repo, CancellationToken ct) => {
      var ok = await repo.DeleteAsync(id, ct).ConfigureAwait(false);
      return ok ? Results.NoContent() : Results.NotFound();
    })
    .WithName("DeleteCommand")
    .WithTags("Commands");

    app.MapPost("/commands/{id}/force-execute", async (string id, string sessionId, GameBot.Service.Services.ICommandExecutor exec, CancellationToken ct) => {
      try {
        var accepted = await exec.ForceExecuteAsync(sessionId, id, ct).ConfigureAwait(false);
        return Results.Accepted($"/sessions/{sessionId}", new { accepted });
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

    app.MapPost("/commands/{id}/evaluate-and-execute", async (string id, string sessionId, GameBot.Service.Services.ICommandExecutor exec, CancellationToken ct) => {
      try {
        var decision = await exec.EvaluateAndExecuteAsync(sessionId, id, ct).ConfigureAwait(false);
        return Results.Accepted($"/sessions/{sessionId}", new {
          accepted = decision.Accepted,
          triggerStatus = decision.TriggerStatus.ToString(),
          message = decision.Reason
        });
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
