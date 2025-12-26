using System.Collections.ObjectModel;
using System.Text.Json;
using GameBot.Domain.Actions;
using GameBot.Service.Models;
using Microsoft.AspNetCore.OpenApi;

namespace GameBot.Service.Endpoints;

internal static class ActionsEndpoints {
  public static IEndpointRouteBuilder MapActionEndpoints(this IEndpointRouteBuilder app) {
    app.MapPost("/api/actions", async (HttpRequest http, IActionRepository repo, CancellationToken ct) => {
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
        return Results.Created($"/api/actions/{created.Id}", new { id = created.Id, name = created.Name });
      }

      // Domain shape fallback
      var req = root.Deserialize<CreateActionRequest>();
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
      return Results.Created($"/api/actions/{createdDomain.Id}", new ActionResponse {
        Id = createdDomain.Id,
        Name = createdDomain.Name,
        GameId = createdDomain.GameId,
        Steps = new Collection<InputActionDto>(createdDomain.Steps.Select(s => new InputActionDto { Type = s.Type, Args = s.Args, DelayMs = s.DelayMs, DurationMs = s.DurationMs }).ToList()),
        Checkpoints = new Collection<string>(createdDomain.Checkpoints.ToList())
      });
    })
    .WithName("CreateAction")
    .WithTags("Actions");

    app.MapGet("/api/actions/{id}", async (string id, IActionRepository repo, CancellationToken ct) => {
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

    app.MapGet("/api/actions", async (string? gameId, IActionRepository repo, CancellationToken ct) => {
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

    app.MapPut("/api/actions/{id}", async (string id, HttpRequest http, IActionRepository repo, CancellationToken ct) => {
      using var doc = await JsonDocument.ParseAsync(http.Body, cancellationToken: ct).ConfigureAwait(false);
      var root = doc.RootElement;
      var existing = await repo.GetAsync(id, ct).ConfigureAwait(false);
      if (existing is null) return Results.NotFound();
      // Authoring shape: allow updating name
      if (root.TryGetProperty("name", out var nameProp) && nameProp.ValueKind == JsonValueKind.String) {
        var name = nameProp.GetString()!.Trim();
        if (!string.IsNullOrWhiteSpace(name)) existing.Name = name;
      }
      var updated = await repo.UpdateAsync(existing, ct).ConfigureAwait(false);
      if (updated is null) return Results.NotFound();
      return Results.Ok(new { id = updated.Id, name = updated.Name });
    }).WithName("UpdateAction").WithTags("Actions");

    app.MapDelete("/api/actions/{id}", (string id) => Results.Conflict(new { error = new { code = "delete_blocked", message = "Deletion not supported for actions via authoring API.", hint = (string?)null } }))
      .WithName("DeleteAction")
      .WithTags("Actions");

    return app;
  }
}
