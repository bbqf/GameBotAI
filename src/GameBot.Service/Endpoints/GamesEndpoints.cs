using GameBot.Domain.Games;
using GameBot.Service.Models;

namespace GameBot.Service.Endpoints;

internal static class GamesEndpoints
{
    public static IEndpointRouteBuilder MapGameEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/games", async (CreateGameRequest req, IGameRepository repo, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(req.Name))
                return Results.BadRequest(new { error = new { code = "invalid_request", message = "name is required", hint = (string?)null } });

            var created = await repo.AddAsync(new GameArtifact
            {
                Id = string.Empty,
                Name = req.Name,
                Description = req.Description
            }, ct).ConfigureAwait(false);

            return Results.Created($"/games/{created.Id}", new GameResponse
            {
                Id = created.Id,
                Name = created.Name,
                Description = created.Description
            });
        }).WithName("CreateGame").WithOpenApi();

        app.MapGet("/games/{id}", async (string id, IGameRepository repo, CancellationToken ct) =>
        {
            var g = await repo.GetAsync(id, ct).ConfigureAwait(false);
            return g is null
                ? Results.NotFound(new { error = new { code = "not_found", message = "Game not found", hint = (string?)null } })
                : Results.Ok(new GameResponse
                {
                    Id = g.Id, Name = g.Name, Description = g.Description
                });
        }).WithName("GetGame").WithOpenApi();

        app.MapGet("/games", async (IGameRepository repo, CancellationToken ct) =>
        {
            var list = await repo.ListAsync(ct).ConfigureAwait(false);
            var resp = list.Select(g => new GameResponse { Id = g.Id, Name = g.Name, Description = g.Description });
            return Results.Ok(resp);
        }).WithName("ListGames").WithOpenApi();

        return app;
    }
}
