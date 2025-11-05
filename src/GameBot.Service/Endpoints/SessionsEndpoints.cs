using GameBot.Domain.Sessions;
using GameBot.Emulator.Session;
using GameBot.Service.Models;

namespace GameBot.Service.Endpoints;

public static class SessionsEndpoints
{
    public static IEndpointRouteBuilder MapSessionEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/sessions", (CreateSessionRequest req, ISessionManager mgr) =>
        {
            var game = req.GameId ?? req.GamePath;
            if (string.IsNullOrWhiteSpace(game))
                return Results.BadRequest(new { error = new { code = "invalid_request", message = "Provide gameId or gamePath.", hint = (string?)null } });

            if (!mgr.CanCreateSession)
            {
                return Results.StatusCode(StatusCodes.Status429TooManyRequests,
                    new { error = new { code = "capacity_exceeded", message = "Max concurrent sessions reached.", hint = (string?)null } });
            }

            var sess = mgr.CreateSession(game, req.ProfileId);
            var resp = new CreateSessionResponse { Id = sess.Id, Status = sess.Status.ToString().ToLowerInvariant(), GameId = sess.GameId };
            return Results.Created($"/sessions/{sess.Id}", resp);
        }).WithName("CreateSession").WithOpenApi();

        app.MapGet("/sessions/{id}", (string id, ISessionManager mgr) =>
        {
            var s = mgr.GetSession(id);
            return s is null
                ? Results.NotFound(new { error = new { code = "not_found", message = "Session not found", hint = (string?)null } })
                : Results.Ok(new { id = s.Id, status = s.Status.ToString().ToLowerInvariant(), uptime = (long)s.Uptime.TotalSeconds, health = s.Health.ToString().ToLowerInvariant(), gameId = s.GameId });
        }).WithName("GetSession").WithOpenApi();

        app.MapPost("/sessions/{id}/inputs", (string id, InputActionsRequest req, ISessionManager mgr) =>
        {
            if (req.Actions is null || req.Actions.Count == 0)
                return Results.BadRequest(new { error = new { code = "invalid_request", message = "No actions provided.", hint = (string?)null } });

            var accepted = mgr.SendInputs(id, req.Actions.Select(a => new InputAction(a.Type, a.Args, a.DelayMs, a.DurationMs)));
            if (accepted == 0) return Results.Conflict(new { error = new { code = "not_running", message = "Session not running.", hint = (string?)null } });
            return Results.Accepted($"/sessions/{id}", new { accepted });
        }).WithName("SendInputs").WithOpenApi();

        app.MapGet("/sessions/{id}/snapshot", async (string id, ISessionManager mgr, CancellationToken ct) =>
        {
            try
            {
                var png = await mgr.GetSnapshotAsync(id, ct);
                return Results.File(png, contentType: "image/png");
            }
            catch (KeyNotFoundException)
            {
                return Results.NotFound(new { error = new { code = "not_found", message = "Session not found", hint = (string?)null } });
            }
        }).WithName("GetSnapshot").WithOpenApi();

        app.MapDelete("/sessions/{id}", (string id, ISessionManager mgr) =>
        {
            var stopped = mgr.StopSession(id);
            return stopped ? Results.Accepted($"/sessions/{id}", new { status = "stopping" })
                           : Results.NotFound(new { error = new { code = "not_found", message = "Session not found", hint = (string?)null } });
        }).WithName("StopSession").WithOpenApi();

        return app;
    }
}
