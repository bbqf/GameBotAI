using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using GameBot.Domain.Games;
using GameBot.Domain.Queues;
using GameBot.Service;
using GameBot.Service.Models;

namespace GameBot.Service.Endpoints;

internal static class GamesEndpoints {
  public static IEndpointRouteBuilder MapGameEndpoints(this IEndpointRouteBuilder app) {
    var group = app.MapGroup(ApiRoutes.Games).WithTags("Games");

    group.MapPost("", async (HttpRequest http, IGameRepository repo, CancellationToken ct) => {
      using var doc = await JsonDocument.ParseAsync(http.Body, cancellationToken: ct).ConfigureAwait(false);
      var root = doc.RootElement;
      if (root.TryGetProperty("name", out var nameProp) && nameProp.ValueKind == JsonValueKind.String) {
        var name = nameProp.GetString()!.Trim();
        if (string.IsNullOrWhiteSpace(name))
          return Results.BadRequest(new { error = new { code = "invalid_request", message = "name is required", hint = (string?)null } });
        string? description = null;
        if (root.TryGetProperty("metadata", out var metaProp)) {
          description = metaProp.GetRawText();
        }
        else if (root.TryGetProperty("description", out var descProp) && descProp.ValueKind == JsonValueKind.String) {
          description = descProp.GetString();
        }
        string? packageName = root.TryGetProperty("packageName", out var pkgProp) && pkgProp.ValueKind == JsonValueKind.String
          ? pkgProp.GetString()
          : null;
        var created = await repo.AddAsync(new GameArtifact {
          Id = string.Empty,
          Name = name,
          Description = description,
          PackageName = packageName
        }, ct).ConfigureAwait(false);
        if (description is not null && IsJsonObject(description)) {
          return Results.Created($"{ApiRoutes.Games}/{created.Id}", new { id = created.Id, name = created.Name, packageName = created.PackageName, metadata = JsonSerializer.Deserialize<object>(description)! });
        }
        return Results.Created($"{ApiRoutes.Games}/{created.Id}", new GameResponse { Id = created.Id, Name = created.Name, Description = created.Description, PackageName = created.PackageName });
      }
      // Fallback to domain DTO
      var req = root.Deserialize<CreateGameRequest>();
      if (req is null || string.IsNullOrWhiteSpace(req.Name))
        return Results.BadRequest(new { error = new { code = "invalid_request", message = "name is required", hint = (string?)null } });
      var createdDomain = await repo.AddAsync(new GameArtifact { Id = string.Empty, Name = req.Name, Description = req.Description }, ct).ConfigureAwait(false);
      return Results.Created($"{ApiRoutes.Games}/{createdDomain.Id}", new GameResponse { Id = createdDomain.Id, Name = createdDomain.Name, Description = createdDomain.Description, PackageName = createdDomain.PackageName });
    }).WithName("CreateGame");

    group.MapGet("{id}", async (string id, IGameRepository repo, CancellationToken ct) => {
      var g = await repo.GetAsync(id, ct).ConfigureAwait(false);
      if (g is null) return Results.NotFound(new { error = new { code = "not_found", message = "Game not found", hint = (string?)null } });
      if (IsJsonObject(g.Description)) {
        return Results.Ok(new { id = g.Id, name = g.Name, packageName = g.PackageName, metadata = JsonSerializer.Deserialize<object>(g.Description!)! });
      }
      return Results.Ok(new GameResponse { Id = g.Id, Name = g.Name, Description = g.Description, PackageName = g.PackageName });
    }).WithName("GetGame");

    group.MapGet("", async (IGameRepository repo, CancellationToken ct) => {
      var list = await repo.ListAsync(ct).ConfigureAwait(false);
      var resp = new System.Collections.Generic.List<object>(list.Count);
      foreach (var g in list) {
        if (IsJsonObject(g.Description)) {
          resp.Add(new { id = g.Id, name = g.Name, packageName = g.PackageName, metadata = JsonSerializer.Deserialize<object>(g.Description!)! });
        }
        else {
          resp.Add(new { id = g.Id, name = g.Name, packageName = g.PackageName });
        }
      }
      return Results.Ok(resp);
    }).WithName("ListGames");

    group.MapPut("{id}", async (string id, HttpRequest http, IGameRepository repo, CancellationToken ct) => {
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
      }
      else if (root.TryGetProperty("description", out var descProp) && descProp.ValueKind == JsonValueKind.String) {
        existing.Description = descProp.GetString();
      }

      // Update packageName when provided (null clears it)
      if (root.TryGetProperty("packageName", out var pkgProp)) {
        existing.PackageName = pkgProp.ValueKind == JsonValueKind.String ? pkgProp.GetString() : null;
      }

      var saved = await repo.UpdateAsync(existing, ct).ConfigureAwait(false);
      if (saved is null) return Results.NotFound(new { error = new { code = "not_found", message = "Game not found", hint = (string?)null } });

      if (IsJsonObject(saved.Description)) {
        return Results.Ok(new { id = saved.Id, name = saved.Name, packageName = saved.PackageName, metadata = JsonSerializer.Deserialize<object>(saved.Description!)! });
      }
      return Results.Ok(new GameResponse { Id = saved.Id, Name = saved.Name, Description = saved.Description, PackageName = saved.PackageName });
    }).WithName("UpdateGame");

    group.MapDelete("{id}", async (string id, IGameRepository games, IQueueRepository queues, CancellationToken ct) => {
      var existing = await games.GetAsync(id, ct).ConfigureAwait(false);
      if (existing is null) return Results.NotFound(new { error = new { code = "not_found", message = "Game not found", hint = (string?)null } });
      var allQueues = await queues.ListAsync().ConfigureAwait(false);
      var referencingQueues = allQueues
        .Where(q => string.Equals(q.LinkedGameId, id, StringComparison.Ordinal))
        .Select(q => new { id = q.Id, name = q.Name })
        .ToList();
      if (referencingQueues.Count > 0) {
        return Results.Json(new {
          error = new { code = "conflict", message = "Game is linked to one or more queues. Unlink before deleting.", hint = (string?)null },
          references = new Dictionary<string, object> { ["queues"] = referencingQueues }
        }, statusCode: 409);
      }
      var deleted = await games.DeleteAsync(id, ct).ConfigureAwait(false);
      return deleted ? Results.NoContent() : Results.NotFound(new { error = new { code = "not_found", message = "Game not found", hint = (string?)null } });
    })
      .WithName("DeleteGame");

    return app;
  }

  private static bool IsJsonObject(string? text) {
    if (string.IsNullOrWhiteSpace(text)) return false;
    try {
      using var doc = JsonDocument.Parse(text);
      return doc.RootElement.ValueKind == JsonValueKind.Object;
    }
    catch { return false; }
  }
}
