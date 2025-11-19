using System.Collections.ObjectModel;
using GameBot.Domain.Profiles;
using GameBot.Service.Models;

namespace GameBot.Service.Endpoints;

internal static class ProfilesEndpoints
{
    public static IEndpointRouteBuilder MapProfileEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/profiles", async (CreateProfileRequest req, IProfileRepository repo, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(req.Name) || string.IsNullOrWhiteSpace(req.GameId))
                return Results.BadRequest(new { error = new { code = "invalid_request", message = "name, gameId are required", hint = (string?)null } });

            var profile = new AutomationProfile
            {
                Id = string.Empty,
                Name = req.Name,
                GameId = req.GameId,
                Steps = new Collection<InputAction>(req.Steps.Select(s => new InputAction { Type = s.Type, Args = s.Args, DelayMs = s.DelayMs, DurationMs = s.DurationMs }).ToList()),
                Checkpoints = new Collection<string>(req.Checkpoints.ToList())
            };

            var created = await repo.AddAsync(profile, ct).ConfigureAwait(false);
            return Results.Created($"/profiles/{created.Id}", new ProfileResponse
            {
                Id = created.Id,
                Name = created.Name,
                GameId = created.GameId,
                Steps = new Collection<InputActionDto>(created.Steps.Select(s => new InputActionDto { Type = s.Type, Args = s.Args, DelayMs = s.DelayMs, DurationMs = s.DurationMs }).ToList()),
                Checkpoints = new Collection<string>(created.Checkpoints.ToList())
            });
        })
        .WithName("CreateProfile")
        .WithTags("Profiles (legacy)");

        app.MapGet("/profiles/{id}", async (string id, IProfileRepository repo, CancellationToken ct) =>
        {
            var p = await repo.GetAsync(id, ct).ConfigureAwait(false);
            return p is null
                ? Results.NotFound(new { error = new { code = "not_found", message = "Profile not found", hint = (string?)null } })
                : Results.Ok(new ProfileResponse
                {
                    Id = p.Id,
                    Name = p.Name,
                    GameId = p.GameId,
                    Steps = new Collection<InputActionDto>(p.Steps.Select(s => new InputActionDto { Type = s.Type, Args = s.Args, DelayMs = s.DelayMs, DurationMs = s.DurationMs }).ToList()),
                    Checkpoints = new Collection<string>(p.Checkpoints.ToList())
                });
        })
        .WithName("GetProfile")
        .WithTags("Profiles (legacy)");

        app.MapGet("/profiles", async (string? gameId, IProfileRepository repo, CancellationToken ct) =>
        {
            var list = await repo.ListAsync(gameId, ct).ConfigureAwait(false);
            var resp = list.Select(p => new ProfileResponse
            {
                Id = p.Id,
                Name = p.Name,
                GameId = p.GameId,
                Steps = new Collection<InputActionDto>(p.Steps.Select(s => new InputActionDto { Type = s.Type, Args = s.Args, DelayMs = s.DelayMs, DurationMs = s.DurationMs }).ToList()),
                Checkpoints = new Collection<string>(p.Checkpoints.ToList())
            });
            return Results.Ok(resp);
        })
        .WithName("ListProfiles")
        .WithTags("Profiles (legacy)");

        return app;
    }
}
