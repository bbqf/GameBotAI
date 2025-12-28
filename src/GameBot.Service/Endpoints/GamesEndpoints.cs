using System.Text.Json;
using System.Linq;
using GameBot.Domain.Games;
using GameBot.Service.Models;

namespace GameBot.Service.Endpoints;

internal static class GamesEndpoints {
  public static IEndpointRouteBuilder MapGameEndpoints(this IEndpointRouteBuilder app) {
    app.MapPost("/api/games", async (HttpRequest http, IGameRepository repo, CancellationToken ct) => {
      using var doc = await JsonDocument.ParseAsync(http.Body, cancellationToken: ct).ConfigureAwait(false);
      var root = doc.RootElement;
      if (root.TryGetProperty("name", out var nameProp) && nameProp.ValueKind == JsonValueKind.String) {
        var name = nameProp.GetString()!.Trim();
        if (string.IsNullOrWhiteSpace(name))
          return Results.BadRequest(new { error = new { code = "invalid_request", message = "name is required", hint = (string?)null } });
        string? description = null;
        if (root.TryGetProperty("metadata", out var metaProp)) {
          // Store metadata as JSON string in Description to avoid changing domain storage
          description = metaProp.GetRawText();
        } else if (root.TryGetProperty("description", out var descProp) && descProp.ValueKind == JsonValueKind.String) {
          description = descProp.GetString();
        }
        var created = await repo.AddAsync(new GameArtifact {
          Id = string.Empty,
          Name = name,
          Description = description
        }, ct).ConfigureAwait(false);
        // Return authoring shape when metadata provided
        if (description is not null && IsJsonObject(description)) {
          return Results.Created($"/api/games/{created.Id}", new { id = created.Id, name = created.Name, metadata = JsonSerializer.Deserialize<object>(description)! });
        }
        return Results.Created($"/api/games/{created.Id}", new GameResponse { Id = created.Id, Name = created.Name, Description = created.Description });
      }
      // Fallback to domain DTO
      var req = root.Deserialize<CreateGameRequest>();
      if (req is null || string.IsNullOrWhiteSpace(req.Name))
        return Results.BadRequest(new { error = new { code = "invalid_request", message = "name is required", hint = (string?)null } });
      var createdDomain = await repo.AddAsync(new GameArtifact { Id = string.Empty, Name = req.Name, Description = req.Description }, ct).ConfigureAwait(false);
      return Results.Created($"/api/games/{createdDomain.Id}", new GameResponse { Id = createdDomain.Id, Name = createdDomain.Name, Description = createdDomain.Description });
    }).WithName("CreateGame");

    // Back-compat alias without /api prefix
    app.MapPost("/games", async (HttpRequest http, IGameRepository repo, CancellationToken ct) => {
      using var doc = await JsonDocument.ParseAsync(http.Body, cancellationToken: ct).ConfigureAwait(false);
      var root = doc.RootElement;
      if (root.TryGetProperty("name", out var nameProp) && nameProp.ValueKind == JsonValueKind.String) {
        var name = nameProp.GetString()!.Trim();
        if (string.IsNullOrWhiteSpace(name))
          return Results.BadRequest(new { error = new { code = "invalid_request", message = "name is required", hint = (string?)null } });
        string? description = null;
        if (root.TryGetProperty("metadata", out var metaProp)) {
          description = metaProp.GetRawText();
        } else if (root.TryGetProperty("description", out var descProp) && descProp.ValueKind == JsonValueKind.String) {
          description = descProp.GetString();
        }
        var created = await repo.AddAsync(new GameArtifact { Id = string.Empty, Name = name, Description = description }, ct).ConfigureAwait(false);
        if (description is not null && IsJsonObject(description)) {
          return Results.Created($"/games/{created.Id}", new { id = created.Id, name = created.Name, metadata = JsonSerializer.Deserialize<object>(description)! });
        }
        return Results.Created($"/games/{created.Id}", new GameResponse { Id = created.Id, Name = created.Name, Description = created.Description });
      }
      var req = root.Deserialize<CreateGameRequest>();
      if (req is null || string.IsNullOrWhiteSpace(req.Name))
        return Results.BadRequest(new { error = new { code = "invalid_request", message = "name is required", hint = (string?)null } });
      var createdDomain = await repo.AddAsync(new GameArtifact { Id = string.Empty, Name = req.Name, Description = req.Description }, ct).ConfigureAwait(false);
      return Results.Created($"/games/{createdDomain.Id}", new GameResponse { Id = createdDomain.Id, Name = createdDomain.Name, Description = createdDomain.Description });
    }).WithName("CreateGameAlias");

    app.MapGet("/api/games/{id}", async (string id, IGameRepository repo, CancellationToken ct) => {
      var g = await repo.GetAsync(id, ct).ConfigureAwait(false);
      if (g is null) return Results.NotFound(new { error = new { code = "not_found", message = "Game not found", hint = (string?)null } });
      if (IsJsonObject(g.Description)) {
        return Results.Ok(new { id = g.Id, name = g.Name, metadata = JsonSerializer.Deserialize<object>(g.Description!)! });
      }
      return Results.Ok(new GameResponse { Id = g.Id, Name = g.Name, Description = g.Description });
    }).WithName("GetGame");

    app.MapGet("/games/{id}", async (string id, IGameRepository repo, CancellationToken ct) => {
      var g = await repo.GetAsync(id, ct).ConfigureAwait(false);
      if (g is null) return Results.NotFound(new { error = new { code = "not_found", message = "Game not found", hint = (string?)null } });
      if (IsJsonObject(g.Description)) {
        return Results.Ok(new { id = g.Id, name = g.Name, metadata = JsonSerializer.Deserialize<object>(g.Description!)! });
      }
      return Results.Ok(new GameResponse { Id = g.Id, Name = g.Name, Description = g.Description });
    }).WithName("GetGameAlias");

    app.MapGet("/api/games", async (IGameRepository repo, CancellationToken ct) => {
      var list = await repo.ListAsync(ct).ConfigureAwait(false);
      var resp = new System.Collections.Generic.List<object>(list.Count);
      foreach (var g in list) {
        if (IsJsonObject(g.Description)) {
          resp.Add(new { id = g.Id, name = g.Name, metadata = JsonSerializer.Deserialize<object>(g.Description!)! });
        } else {
          resp.Add(new { id = g.Id, name = g.Name });
        }
      }
      return Results.Ok(resp);
    }).WithName("ListGames");

    app.MapGet("/games", async (IGameRepository repo, CancellationToken ct) => {
      var list = await repo.ListAsync(ct).ConfigureAwait(false);
      var resp = new System.Collections.Generic.List<object>(list.Count);
      foreach (var g in list) {
        if (IsJsonObject(g.Description)) {
          resp.Add(new { id = g.Id, name = g.Name, metadata = JsonSerializer.Deserialize<object>(g.Description!)! });
        } else {
          resp.Add(new { id = g.Id, name = g.Name });
        }
      }
      return Results.Ok(resp);
    }).WithName("ListGamesAlias");

    app.MapPut("/api/games/{id}", async (string id, HttpRequest http, IGameRepository repo, CancellationToken ct) => {
      var existing = await repo.GetAsync(id, ct).ConfigureAwait(false);
      if (existing is null) return Results.NotFound(new { error = new { code = "not_found", message = "Game not found", hint = (string?)null } });

      using var doc = await JsonDocument.ParseAsync(http.Body, cancellationToken: ct).ConfigureAwait(false);
      var root = doc.RootElement;

      // Update name when provided
      if (root.TryGetProperty("name", out var nameProp) && nameProp.ValueKind == JsonValueKind.String) {
        var name = nameProp.GetString()!.Trim();
        if (string.IsNullOrWhiteSpace(name)) {
          return Results.BadRequest(new { error = new { code = "invalid_request", message = "name is required", hint = (string?)null } });
        }
        existing.Name = name;
      }

      // Update description/metadata
      if (root.TryGetProperty("metadata", out var metaProp)) {
        existing.Description = metaProp.GetRawText();
      } else if (root.TryGetProperty("description", out var descProp) && descProp.ValueKind == JsonValueKind.String) {
        existing.Description = descProp.GetString();
      }

      var saved = await repo.UpdateAsync(existing, ct).ConfigureAwait(false);
      if (saved is null) return Results.NotFound(new { error = new { code = "not_found", message = "Game not found", hint = (string?)null } });

      if (IsJsonObject(saved.Description)) {
        return Results.Ok(new { id = saved.Id, name = saved.Name, metadata = JsonSerializer.Deserialize<object>(saved.Description!)! });
      }
      return Results.Ok(new GameResponse { Id = saved.Id, Name = saved.Name, Description = saved.Description });
    }).WithName("UpdateGame").WithTags("Games");

    app.MapPut("/games/{id}", async (string id, HttpRequest http, IGameRepository repo, CancellationToken ct) => {
      var existing = await repo.GetAsync(id, ct).ConfigureAwait(false);
      if (existing is null) return Results.NotFound(new { error = new { code = "not_found", message = "Game not found", hint = (string?)null } });

      using var doc = await JsonDocument.ParseAsync(http.Body, cancellationToken: ct).ConfigureAwait(false);
      var root = doc.RootElement;

      if (root.TryGetProperty("name", out var nameProp) && nameProp.ValueKind == JsonValueKind.String) {
        var name = nameProp.GetString()!.Trim();
        if (string.IsNullOrWhiteSpace(name)) {
          return Results.BadRequest(new { error = new { code = "invalid_request", message = "name is required", hint = (string?)null } });
        }
        existing.Name = name;
      }

      if (root.TryGetProperty("metadata", out var metaProp)) {
        existing.Description = metaProp.GetRawText();
      } else if (root.TryGetProperty("description", out var descProp) && descProp.ValueKind == JsonValueKind.String) {
        existing.Description = descProp.GetString();
      }

      var saved = await repo.UpdateAsync(existing, ct).ConfigureAwait(false);
      if (saved is null) return Results.NotFound(new { error = new { code = "not_found", message = "Game not found", hint = (string?)null } });

      if (IsJsonObject(saved.Description)) {
        return Results.Ok(new { id = saved.Id, name = saved.Name, metadata = JsonSerializer.Deserialize<object>(saved.Description!)! });
      }
      return Results.Ok(new GameResponse { Id = saved.Id, Name = saved.Name, Description = saved.Description });
    }).WithName("UpdateGameAlias").WithTags("Games");

    // Authoring delete: allow when game is unreferenced; otherwise return 409 with references.
    app.MapDelete("/api/games/{id}", async (string id, IGameRepository games, GameBot.Domain.Actions.IActionRepository actions, CancellationToken ct) => {
      var existing = await games.GetAsync(id, ct).ConfigureAwait(false);
      if (existing is null) return Results.NotFound(new { error = new { code = "not_found", message = "Game not found", hint = (string?)null } });
      var actionList = await actions.ListAsync(id, ct).ConfigureAwait(false);
      if (actionList.Count > 0) {
        var refs = actionList.Select(a => new { id = a.Id, name = a.Name }).ToArray();
        return Results.Conflict(new { error = new { code = "delete_blocked", message = "Game is referenced by actions.", hint = (string?)null }, references = new { actions = refs } });
      }
      var deleted = await games.DeleteAsync(id, ct).ConfigureAwait(false);
      return deleted ? Results.NoContent() : Results.NotFound(new { error = new { code = "not_found", message = "Game not found", hint = (string?)null } });
    })
      .WithName("DeleteGame")
      .WithTags("Games");

    app.MapDelete("/games/{id}", async (string id, IGameRepository games, GameBot.Domain.Actions.IActionRepository actions, CancellationToken ct) => {
      var existing = await games.GetAsync(id, ct).ConfigureAwait(false);
      if (existing is null) return Results.NotFound(new { error = new { code = "not_found", message = "Game not found", hint = (string?)null } });
      var actionList = await actions.ListAsync(id, ct).ConfigureAwait(false);
      if (actionList.Count > 0) {
        var refs = actionList.Select(a => new { id = a.Id, name = a.Name }).ToArray();
        return Results.Conflict(new { error = new { code = "delete_blocked", message = "Game is referenced by actions.", hint = (string?)null }, references = new { actions = refs } });
      }
      var deleted = await games.DeleteAsync(id, ct).ConfigureAwait(false);
      return deleted ? Results.NoContent() : Results.NotFound(new { error = new { code = "not_found", message = "Game not found", hint = (string?)null } });
    }).WithName("DeleteGameAlias");

    return app;
  }

  private static bool IsJsonObject(string? text) {
    if (string.IsNullOrWhiteSpace(text)) return false;
    try {
      using var doc = JsonDocument.Parse(text);
      return doc.RootElement.ValueKind == JsonValueKind.Object;
    } catch { return false; }
  }
}
