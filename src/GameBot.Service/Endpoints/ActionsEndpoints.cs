using System.Collections.ObjectModel;
using GameBot.Domain.Actions;
using GameBot.Domain.Profiles;
using GameBot.Service.Models;

namespace GameBot.Service.Endpoints;

internal static class ActionsEndpoints
{
    public static IEndpointRouteBuilder MapActionEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/actions", async (CreateActionRequest req, IActionRepository repo, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(req.Name) || string.IsNullOrWhiteSpace(req.GameId))
                return Results.BadRequest(new { error = new { code = "invalid_request", message = "name, gameId are required", hint = (string?)null } });

            var action = new Domain.Actions.Action
            {
                Id = string.Empty,
                Name = req.Name,
                GameId = req.GameId,
                Steps = new Collection<InputAction>(req.Steps.Select(s => new InputAction { Type = s.Type, Args = s.Args, DelayMs = s.DelayMs, DurationMs = s.DurationMs }).ToList()),
                Checkpoints = new Collection<string>(req.Checkpoints.ToList())
            };

            var created = await repo.AddAsync(action, ct).ConfigureAwait(false);
            return Results.Created($"/actions/{created.Id}", new ActionResponse
            {
                Id = created.Id,
                Name = created.Name,
                GameId = created.GameId,
                Steps = new Collection<InputActionDto>(created.Steps.Select(s => new InputActionDto { Type = s.Type, Args = s.Args, DelayMs = s.DelayMs, DurationMs = s.DurationMs }).ToList()),
                Checkpoints = new Collection<string>(created.Checkpoints.ToList())
            });
        }).WithName("CreateAction");

        app.MapGet("/actions/{id}", async (string id, IActionRepository repo, CancellationToken ct) =>
        {
            var a = await repo.GetAsync(id, ct).ConfigureAwait(false);
            return a is null
                ? Results.NotFound(new { error = new { code = "not_found", message = "Action not found", hint = (string?)null } })
                : Results.Ok(new ActionResponse
                {
                    Id = a.Id,
                    Name = a.Name,
                    GameId = a.GameId,
                    Steps = new Collection<InputActionDto>(a.Steps.Select(s => new InputActionDto { Type = s.Type, Args = s.Args, DelayMs = s.DelayMs, DurationMs = s.DurationMs }).ToList()),
                    Checkpoints = new Collection<string>(a.Checkpoints.ToList())
                });
        }).WithName("GetAction");

        app.MapGet("/actions", async (string? gameId, IActionRepository repo, CancellationToken ct) =>
        {
            var list = await repo.ListAsync(gameId, ct).ConfigureAwait(false);
            var resp = list.Select(a => new ActionResponse
            {
                Id = a.Id,
                Name = a.Name,
                GameId = a.GameId,
                Steps = new Collection<InputActionDto>(a.Steps.Select(s => new InputActionDto { Type = s.Type, Args = s.Args, DelayMs = s.DelayMs, DurationMs = s.DurationMs }).ToList()),
                Checkpoints = new Collection<string>(a.Checkpoints.ToList())
            });
            return Results.Ok(resp);
        }).WithName("ListActions");

        return app;
    }
}
