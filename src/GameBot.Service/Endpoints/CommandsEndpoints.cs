using System.Collections.ObjectModel;
using System.Text.Json;
using GameBot.Domain.Commands;
using GameBot.Service.Models;
using Microsoft.AspNetCore.OpenApi;

namespace GameBot.Service.Endpoints;

internal static class CommandsEndpoints {
  public static IEndpointRouteBuilder MapCommandEndpoints(this IEndpointRouteBuilder app) {
    app.MapPost("/api/commands", async (HttpRequest http, ICommandRepository repo, CancellationToken ct) => {
      using var doc = await JsonDocument.ParseAsync(http.Body, cancellationToken: ct).ConfigureAwait(false);
      var root = doc.RootElement;
      if (root.TryGetProperty("name", out var nameProp) && nameProp.ValueKind == JsonValueKind.String) {
        var name = nameProp.GetString()!.Trim();
        if (string.IsNullOrWhiteSpace(name))
          return Results.BadRequest(new { error = new { code = "invalid_request", message = "name is required", hint = (string?)null } });
        // Authoring shape: actions[] -> steps of type Action
        Collection<CommandStep> steps = new();
        if (root.TryGetProperty("actions", out var actionsProp) && actionsProp.ValueKind == JsonValueKind.Array) {
          int order = 0;
          foreach (var el in actionsProp.EnumerateArray()) {
            if (el.ValueKind == JsonValueKind.String) {
              steps.Add(new CommandStep { Type = CommandStepType.Action, TargetId = el.GetString()!, Order = order++ });
            }
          }
        }
        var created = await repo.AddAsync(new Command { Id = string.Empty, Name = name, TriggerId = null, Steps = steps }, ct).ConfigureAwait(false);
        return Results.Created($"/api/commands/{created.Id}", new { id = created.Id, name = created.Name, actions = steps.Select(s => s.TargetId).ToArray() });
      }

      // Domain shape fallback
      var req = root.Deserialize<CreateCommandRequest>();
      if (req is null || string.IsNullOrWhiteSpace(req.Name))
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

      var createdDomain = await repo.AddAsync(command, ct).ConfigureAwait(false);
      return Results.Created($"/api/commands/{createdDomain.Id}", new CommandResponse {
        Id = createdDomain.Id,
        Name = createdDomain.Name,
        TriggerId = createdDomain.TriggerId,
        Steps = new Collection<CommandStepDto>(createdDomain.Steps.Select(s => new CommandStepDto {
          Type = s.Type == CommandStepType.Action ? CommandStepTypeDto.Action : CommandStepTypeDto.Command,
          TargetId = s.TargetId,
          Order = s.Order
        }).ToList())
      });
    })
    .WithName("CreateCommand")
    .WithTags("Commands");

    app.MapGet("/api/commands/{id}", async (string id, ICommandRepository repo, CancellationToken ct) => {
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

    app.MapGet("/api/commands", async (ICommandRepository repo, CancellationToken ct) => {
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

    app.MapPatch("/api/commands/{id}", async (string id, UpdateCommandRequest req, ICommandRepository repo, CancellationToken ct) => {
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

    app.MapDelete("/api/commands/{id}", async (string id, ICommandRepository repo, CancellationToken ct) => {
      var ok = await repo.DeleteAsync(id, ct).ConfigureAwait(false);
      return ok ? Results.NoContent() : Results.NotFound();
    })
    .WithName("DeleteCommand")
    .WithTags("Commands");

    app.MapPost("/api/commands/{id}/force-execute", async (string id, string sessionId, GameBot.Service.Services.ICommandExecutor exec, CancellationToken ct) => {
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

    app.MapPost("/api/commands/{id}/evaluate-and-execute", async (string id, string sessionId, GameBot.Service.Services.ICommandExecutor exec, CancellationToken ct) => {
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
