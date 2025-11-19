using GameBot.Domain.Sessions;
using GameBot.Emulator.Session;
using GameBot.Service.Models;
using GameBot.Emulator.Adb;
using GameBot.Domain.Actions;

namespace GameBot.Service.Endpoints;

internal static class SessionsEndpoints
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
                return Results.Json(new { error = new { code = "capacity_exceeded", message = "Max concurrent sessions reached.", hint = (string?)null } }, statusCode: StatusCodes.Status429TooManyRequests);
            }
            try
            {
                var sess = mgr.CreateSession(game, req.AdbSerial);
                var resp = new CreateSessionResponse { Id = sess.Id, Status = sess.Status.ToString().ToUpperInvariant(), GameId = sess.GameId };
                return Results.Created($"/sessions/{sess.Id}", resp);
            }
            catch (InvalidOperationException ex) when (ex.Message == "no_adb_devices")
            {
                return Results.NotFound(new { error = new { code = "adb_device_not_found", message = "No ADB devices connected.", hint = "Connect a device or emulator and try again." } });
            }
            catch (KeyNotFoundException ex)
            {
                return Results.NotFound(new { error = new { code = "adb_device_not_found", message = ex.Message, hint = "Check /adb/devices for available serials." } });
            }
        }).WithName("CreateSession");

        app.MapGet("/sessions/{id}", (string id, ISessionManager mgr) =>
        {
            var s = mgr.GetSession(id);
            return s is null
                ? Results.NotFound(new { error = new { code = "not_found", message = "Session not found", hint = (string?)null } })
                : Results.Ok(new { id = s.Id, status = s.Status.ToString().ToUpperInvariant(), uptime = (long)s.Uptime.TotalSeconds, health = s.Health.ToString().ToUpperInvariant(), gameId = s.GameId });
        }).WithName("GetSession");

        // Surface chosen device for this session (if ADB mode bound one)
        app.MapGet("/sessions/{id}/device", (string id, ISessionManager mgr) =>
        {
            var s = mgr.GetSession(id);
            return s is null
                ? Results.NotFound(new { error = new { code = "not_found", message = "Session not found", hint = (string?)null } })
                : Results.Ok(new
                {
                    id = s.Id,
                    deviceSerial = s.DeviceSerial,
                    mode = string.IsNullOrWhiteSpace(s.DeviceSerial) ? "STUB" : "ADB"
                });
        }).WithName("GetSessionDevice");

        app.MapPost("/sessions/{id}/inputs", async (string id, InputActionsRequest req, ISessionManager mgr, CancellationToken ct) =>
        {
            if (req.Actions is null || req.Actions.Count == 0)
                return Results.BadRequest(new { error = new { code = "invalid_request", message = "No actions provided.", hint = (string?)null } });

            var accepted = await mgr.SendInputsAsync(id, req.Actions.Select(a => new GameBot.Emulator.Session.InputAction(a.Type, a.Args, a.DelayMs, a.DurationMs)), ct).ConfigureAwait(false);
            if (accepted == 0) return Results.Conflict(new { error = new { code = "not_running", message = "Session not running.", hint = (string?)null } });
            return Results.Accepted($"/sessions/{id}", new { accepted });
        }).WithName("SendInputs");

        // Session health endpoint (checks ADB connectivity if applicable)
    app.MapGet("/sessions/{id}/health", async (string id, ISessionManager mgr, ILogger<AdbClient> adbLogger, CancellationToken ct) =>
        {
            var s = mgr.GetSession(id);
            if (s is null)
                return Results.NotFound(new { error = new { code = "not_found", message = "Session not found", hint = (string?)null } });

            if (OperatingSystem.IsWindows() && !string.IsNullOrWhiteSpace(s.DeviceSerial))
            {
                try
                {
                    var adb = new GameBot.Emulator.Adb.AdbClient(adbLogger).WithSerial(s.DeviceSerial);
                    var (code, stdout, stderr) = await adb.ExecAsync("get-state", ct).ConfigureAwait(false);
                    var ok = code == 0 && stdout.Trim().Equals("device", StringComparison.OrdinalIgnoreCase);
                    return Results.Ok(new { id = s.Id, mode = "ADB", deviceSerial = s.DeviceSerial, adb = new { ok, stdout, stderr } });
                }
                catch (InvalidOperationException ex)
                {
                    return Results.Ok(new { id = s.Id, mode = "ADB", deviceSerial = s.DeviceSerial, adb = new { ok = false, error = ex.Message } });
                }
            }

            return Results.Ok(new { id = s.Id, mode = "STUB", deviceSerial = (string?)null, adb = new { ok = true } });
        }).WithName("GetSessionHealth");

        app.MapGet("/sessions/{id}/snapshot", async (string id, ISessionManager mgr, CancellationToken ct) =>
        {
            try
            {
                var png = await mgr.GetSnapshotAsync(id, ct).ConfigureAwait(false);
                return Results.File(png, contentType: "image/png");
            }
            catch (KeyNotFoundException)
            {
                return Results.NotFound(new { error = new { code = "not_found", message = "Session not found", hint = (string?)null } });
            }
        }).WithName("GetSnapshot");

        // Execute an Action against a session
        app.MapPost("/sessions/{id}/execute-action", async (string id, string actionId, IActionRepository actions, ISessionManager mgr, CancellationToken ct) =>
        {
            // Validate session exists
            var session = mgr.GetSession(id);
            if (session is null)
                return Results.NotFound(new { error = new { code = "not_found", message = "Session not found", hint = (string?)null } });

            if (session.Status != SessionStatus.Running)
                return Results.Conflict(new { error = new { code = "not_running", message = "Session not running.", hint = (string?)null } });

            var action = await actions.GetAsync(actionId, ct).ConfigureAwait(false);
            if (action is null)
                return Results.NotFound(new { error = new { code = "not_found", message = "Action not found", hint = (string?)null } });

            if (action.Steps.Count == 0)
                return Results.Accepted($"/sessions/{id}", new { accepted = 0 });

            var inputs = action.Steps.Select(a => new GameBot.Emulator.Session.InputAction(a.Type, a.Args, a.DelayMs, a.DurationMs));
            var accepted = await mgr.SendInputsAsync(id, inputs, ct).ConfigureAwait(false);
            return Results.Accepted($"/sessions/{id}", new { accepted });
        }).WithName("ExecuteAction");

        app.MapDelete("/sessions/{id}", (string id, ISessionManager mgr) =>
        {
            var stopped = mgr.StopSession(id);
            return stopped ? Results.Accepted($"/sessions/{id}", new { status = "stopping" })
                           : Results.NotFound(new { error = new { code = "not_found", message = "Session not found", hint = (string?)null } });
        }).WithName("StopSession");

        return app;
    }
}
