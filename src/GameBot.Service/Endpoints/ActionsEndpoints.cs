using System.Collections.ObjectModel;
using System.Text.Json;
using GameBot.Domain.Actions;
using GameBot.Domain.Commands;
using GameBot.Service;
using GameBot.Service.Models;
using Microsoft.AspNetCore.OpenApi;

namespace GameBot.Service.Endpoints;

internal static class ActionsEndpoints {
  private static readonly JsonSerializerOptions WebJsonOptions = new(JsonSerializerDefaults.Web);

  private static ActionResponse ToResponse(Domain.Actions.Action a) => new() {
    Id = a.Id,
    Name = a.Name,
    GameId = a.GameId,
    Steps = new Collection<InputActionDto>(a.Steps.Select(s => new InputActionDto {
      Type = s.Type,
      Args = s.Args,
      DelayMs = s.DelayMs,
      DurationMs = s.DurationMs
    }).ToList()),
    Checkpoints = new Collection<string>(a.Checkpoints.ToList())
  };

  private static void ApplyActionPatch(JsonElement root, Domain.Actions.Action existing) {
    if (root.TryGetProperty("name", out var nameProp) && nameProp.ValueKind == JsonValueKind.String) {
      var name = nameProp.GetString()!.Trim();
      if (!string.IsNullOrWhiteSpace(name)) existing.Name = name;
    }

    if (root.TryGetProperty("gameId", out var gameIdProp) && gameIdProp.ValueKind == JsonValueKind.String) {
      var gameId = gameIdProp.GetString()!.Trim();
      if (!string.IsNullOrWhiteSpace(gameId)) existing.GameId = gameId;
    }

    if (root.TryGetProperty("steps", out var stepsProp) && stepsProp.ValueKind == JsonValueKind.Array) {
      var parsed = stepsProp.Deserialize<Collection<InputAction>>(WebJsonOptions);
      if (parsed is not null) {
        existing.Steps.Clear();
        foreach (var step in parsed) {
          if (string.IsNullOrWhiteSpace(step.Type)) continue;
          existing.Steps.Add(new InputAction {
            Type = step.Type,
            Args = step.Args ?? new Dictionary<string, object>(),
            DelayMs = step.DelayMs,
            DurationMs = step.DurationMs
          });
        }
      }
    }

    if (root.TryGetProperty("checkpoints", out var checkpointsProp) && checkpointsProp.ValueKind == JsonValueKind.Array) {
      var parsed = checkpointsProp.Deserialize<Collection<string>>(WebJsonOptions);
      if (parsed is not null) {
        existing.Checkpoints.Clear();
        foreach (var cp in parsed.Where(c => !string.IsNullOrWhiteSpace(c))) existing.Checkpoints.Add(cp);
      }
    }
  }
  public static IEndpointRouteBuilder MapActionEndpoints(this IEndpointRouteBuilder app) {
    app.MapPost(ApiRoutes.Actions, async (HttpRequest http, IActionRepository repo, CancellationToken ct) => {
      using var doc = await JsonDocument.ParseAsync(http.Body, cancellationToken: ct).ConfigureAwait(false);
      var root = doc.RootElement;
      // Authoring shape: { name, description? }
      if (root.TryGetProperty("name", out var nameProp) && nameProp.ValueKind == JsonValueKind.String && !root.TryGetProperty("gameId", out _)) {
        var name = nameProp.GetString()!.Trim();
        if (string.IsNullOrWhiteSpace(name))
          return Results.BadRequest(new { error = new { code = "invalid_request", message = "name is required", hint = (string?)null } });
        var created = await repo.AddAsync(new Domain.Actions.Action {
          Id = string.Empty,
          Name = name,
          GameId = "authoring",
          Steps = new Collection<InputAction>(),
          Checkpoints = new Collection<string>()
        }, ct).ConfigureAwait(false);
        return Results.Created($"{ApiRoutes.Actions}/{created.Id}", new { id = created.Id, name = created.Name });
      }

      // Domain shape fallback
      var req = root.Deserialize<CreateActionRequest>(WebJsonOptions);
      if (req is null || string.IsNullOrWhiteSpace(req.Name) || string.IsNullOrWhiteSpace(req.GameId))
        return Results.BadRequest(new { error = new { code = "invalid_request", message = "name, gameId are required", hint = (string?)null } });

      var action = new Domain.Actions.Action {
        Id = string.Empty,
        Name = req.Name,
        GameId = req.GameId,
        Steps = new Collection<InputAction>(req.Steps.Select(s => new InputAction { Type = s.Type, Args = s.Args, DelayMs = s.DelayMs, DurationMs = s.DurationMs }).ToList()),
        Checkpoints = new Collection<string>(req.Checkpoints.ToList())
      };

      var createdDomain = await repo.AddAsync(action, ct).ConfigureAwait(false);
      return Results.Created($"{ApiRoutes.Actions}/{createdDomain.Id}", new ActionResponse {
        Id = createdDomain.Id,
        Name = createdDomain.Name,
        GameId = createdDomain.GameId,
        Steps = new Collection<InputActionDto>(createdDomain.Steps.Select(s => new InputActionDto { Type = s.Type, Args = s.Args, DelayMs = s.DelayMs, DurationMs = s.DurationMs }).ToList()),
        Checkpoints = new Collection<string>(createdDomain.Checkpoints.ToList())
      });
    })
    .WithName("CreateAction")
    .WithTags("Actions");

    app.MapGet($"{ApiRoutes.Actions}/{{id}}", async (string id, IActionRepository repo, CancellationToken ct) => {
      var a = await repo.GetAsync(id, ct).ConfigureAwait(false);
      return a is null
          ? Results.NotFound(new { error = new { code = "not_found", message = "Action not found", hint = (string?)null } })
          : Results.Ok(new ActionResponse {
            Id = a.Id,
            Name = a.Name,
            GameId = a.GameId,
            Steps = new Collection<InputActionDto>(a.Steps.Select(s => new InputActionDto { Type = s.Type, Args = s.Args, DelayMs = s.DelayMs, DurationMs = s.DurationMs }).ToList()),
            Checkpoints = new Collection<string>(a.Checkpoints.ToList())
          });
    })
    .WithName("GetAction")
    .WithTags("Actions");

    app.MapGet(ApiRoutes.Actions, async (string? gameId, IActionRepository repo, CancellationToken ct) => {
      var list = await repo.ListAsync(gameId, ct).ConfigureAwait(false);
      var resp = list.Select(a => new ActionResponse {
        Id = a.Id,
        Name = a.Name,
        GameId = a.GameId,
        Steps = new Collection<InputActionDto>(a.Steps.Select(s => new InputActionDto { Type = s.Type, Args = s.Args, DelayMs = s.DelayMs, DurationMs = s.DurationMs }).ToList()),
        Checkpoints = new Collection<string>(a.Checkpoints.ToList())
      });
      return Results.Ok(resp);
    })
    .WithName("ListActions")
    .WithTags("Actions");

    app.MapPatch($"{ApiRoutes.Actions}/{{id}}", async (string id, HttpRequest http, IActionRepository repo, CancellationToken ct) => {
      using var doc = await JsonDocument.ParseAsync(http.Body, cancellationToken: ct).ConfigureAwait(false);
      var root = doc.RootElement;
      var existing = await repo.GetAsync(id, ct).ConfigureAwait(false);
      if (existing is null) return Results.NotFound();

      ApplyActionPatch(root, existing);

      var updated = await repo.UpdateAsync(existing, ct).ConfigureAwait(false);
      if (updated is null) return Results.NotFound();
      return Results.Ok(ToResponse(updated));
    }).WithName("UpdateAction").WithTags("Actions");

    // Back-compat: honor PUT for clients still using it
    app.MapPut($"{ApiRoutes.Actions}/{{id}}", async (string id, HttpRequest http, IActionRepository repo, CancellationToken ct) => {
      using var doc = await JsonDocument.ParseAsync(http.Body, cancellationToken: ct).ConfigureAwait(false);
      var root = doc.RootElement;
      var existing = await repo.GetAsync(id, ct).ConfigureAwait(false);
      if (existing is null) return Results.NotFound();

      ApplyActionPatch(root, existing);

      var updated = await repo.UpdateAsync(existing, ct).ConfigureAwait(false);
      if (updated is null) return Results.NotFound();
      return Results.Ok(ToResponse(updated));
    }).WithName("UpdateActionPut").WithTags("Actions");

    app.MapPost($"{ApiRoutes.Actions}/{{id}}/duplicate", async (string id, IActionRepository repo, CancellationToken ct) => {
      var existing = await repo.GetAsync(id, ct).ConfigureAwait(false);
      if (existing is null) return Results.NotFound(new { error = new { code = "not_found", message = "Action not found", hint = (string?)null } });

      var clone = new Domain.Actions.Action {
        Id = string.Empty,
        Name = $"{existing.Name} copy",
        GameId = existing.GameId,
        Steps = new Collection<InputAction>(existing.Steps.Select(s => new InputAction { Type = s.Type, Args = s.Args, DelayMs = s.DelayMs, DurationMs = s.DurationMs }).ToList()),
        Checkpoints = new Collection<string>(existing.Checkpoints.ToList())
      };

      var created = await repo.AddAsync(clone, ct).ConfigureAwait(false);
      return Results.Created($"{ApiRoutes.Actions}/{created.Id}", new ActionResponse {
        Id = created.Id,
        Name = created.Name,
        GameId = created.GameId,
        Steps = new Collection<InputActionDto>(created.Steps.Select(s => new InputActionDto { Type = s.Type, Args = s.Args, DelayMs = s.DelayMs, DurationMs = s.DurationMs }).ToList()),
        Checkpoints = new Collection<string>(created.Checkpoints.ToList())
      });
    }).WithName("DuplicateAction").WithTags("Actions");

    app.MapDelete($"{ApiRoutes.Actions}/{{id}}", async (string id, IActionRepository actions, ICommandRepository commands, CancellationToken ct) => {
      var existing = await actions.GetAsync(id, ct).ConfigureAwait(false);
      if (existing is null) return Results.NotFound(new { error = new { code = "not_found", message = "Action not found", hint = (string?)null } });
      var cmdList = await commands.ListAsync(ct).ConfigureAwait(false);
      var referencingCommands = cmdList.Where(c => c.Steps.Any(s => s.Type == GameBot.Domain.Commands.CommandStepType.Action && string.Equals(s.TargetId, id, StringComparison.OrdinalIgnoreCase)))
                                       .Select(c => new { id = c.Id, name = c.Name })
                                       .ToArray();
      if (referencingCommands.Length > 0) {
        return Results.Conflict(new { error = new { code = "delete_blocked", message = "Action is referenced by commands.", hint = (string?)null }, references = new { commands = referencingCommands } });
      }
      var deleted = await actions.DeleteAsync(id, ct).ConfigureAwait(false);
      return deleted ? Results.NoContent() : Results.NotFound(new { error = new { code = "not_found", message = "Action not found", hint = (string?)null } });
    })
      .WithName("DeleteAction")
      .WithTags("Actions");

    return app;
  }
}
