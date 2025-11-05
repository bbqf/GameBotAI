using GameBot.Domain.Games;
using GameBot.Service.Models;

namespace GameBot.Service.Endpoints;

internal static class GamesEndpoints
{
    public static IEndpointRouteBuilder MapGameEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/games", async (CreateGameRequest req, IGameRepository repo, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(req.Title) || string.IsNullOrWhiteSpace(req.Path) || string.IsNullOrWhiteSpace(req.Hash))
                return Results.BadRequest(new { error = new { code = "invalid_request", message = "title, path, hash are required", hint = (string?)null } });

            var created = await repo.AddAsync(new GameArtifact
            {
                Id = string.Empty,
                Title = req.Title,
                Path = req.Path,
                Hash = req.Hash,
                Region = req.Region,
                Notes = req.Notes,
                ComplianceAttestation = req.ComplianceAttestation
            }, ct).ConfigureAwait(false);

            return Results.Created($"/games/{created.Id}", new GameResponse
            {
                Id = created.Id,
                Title = created.Title,
                Path = created.Path,
                Hash = created.Hash,
                Region = created.Region,
                Notes = created.Notes,
                ComplianceAttestation = created.ComplianceAttestation
            });
        }).WithName("CreateGame").WithOpenApi();

        app.MapGet("/games/{id}", async (string id, IGameRepository repo, CancellationToken ct) =>
        {
            var g = await repo.GetAsync(id, ct).ConfigureAwait(false);
            return g is null
                ? Results.NotFound(new { error = new { code = "not_found", message = "Game not found", hint = (string?)null } })
                : Results.Ok(new GameResponse
                {
                    Id = g.Id, Title = g.Title, Path = g.Path, Hash = g.Hash, Region = g.Region, Notes = g.Notes, ComplianceAttestation = g.ComplianceAttestation
                });
        }).WithName("GetGame").WithOpenApi();

        app.MapGet("/games", async (IGameRepository repo, CancellationToken ct) =>
        {
            var list = await repo.ListAsync(ct).ConfigureAwait(false);
            var resp = list.Select(g => new GameResponse
            {
                Id = g.Id, Title = g.Title, Path = g.Path, Hash = g.Hash, Region = g.Region, Notes = g.Notes, ComplianceAttestation = g.ComplianceAttestation
            });
            return Results.Ok(resp);
        }).WithName("ListGames").WithOpenApi();

        return app;
    }
}
