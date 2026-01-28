// Suppress CA1515: controllers must remain public for ASP.NET discovery
#pragma warning disable CA1515
using GameBot.Domain.Sessions;
using GameBot.Service.Models;
using Microsoft.AspNetCore.Mvc;
using GameBot.Service.Services;
using Microsoft.AspNetCore.Http;
using System.Linq;

namespace GameBot.Service.Controllers;

[ApiController]
[Route("api/sessions")]
public sealed class SessionsController : ControllerBase
{
    private readonly ISessionService _sessions;

    public SessionsController(ISessionService sessions)
    {
        _sessions = sessions;
    }

    [HttpGet("running")]
    public ActionResult<RunningSessionsResponse> GetRunningSessions()
    {
        var running = _sessions.GetRunningSessions();
        var resp = new RunningSessionsResponse
        {
            Sessions = running.Select(Map).ToArray()
        };
        return Ok(resp);
    }

    [HttpPost("start")]
    public ActionResult<StartSessionResponse> StartSession([FromBody] StartSessionRequest request)
    {
        if (request is null)
        {
            return BadRequest(new { error = new { code = "invalid_request", message = "request body is required.", hint = (string?)null } });
        }
        if (string.IsNullOrWhiteSpace(request.GameId) || string.IsNullOrWhiteSpace(request.EmulatorId))
        {
            return BadRequest(new { error = new { code = "invalid_request", message = "gameId and emulatorId are required.", hint = (string?)null } });
        }

        try
        {
            var started = _sessions.StartSession(request.GameId, request.EmulatorId);
            var running = _sessions.GetRunningSessions();
            return Ok(new StartSessionResponse
            {
                SessionId = started.SessionId,
                RunningSessions = running.Select(Map).ToArray()
            });
        }
        catch (InvalidOperationException ex) when (string.Equals(ex.Message, "capacity_exceeded", StringComparison.OrdinalIgnoreCase))
        {
            return StatusCode(StatusCodes.Status429TooManyRequests, new { error = new { code = "capacity_exceeded", message = "Max concurrent sessions reached.", hint = (string?)null } });
        }
        catch (InvalidOperationException ex) when (string.Equals(ex.Message, "no_adb_devices", StringComparison.OrdinalIgnoreCase))
        {
            return NotFound(new { error = new { code = "adb_device_not_found", message = "No ADB devices connected.", hint = "Connect a device or emulator and try again." } });
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { error = new { code = "adb_device_not_found", message = ex.Message, hint = "Check /adb/devices for available serials." } });
        }
    }

    [HttpPost("stop")]
    public IActionResult StopSession([FromBody] StopSessionRequest request)
    {
        if (request is null)
        {
            return BadRequest(new { error = new { code = "invalid_request", message = "request body is required.", hint = (string?)null } });
        }
        if (string.IsNullOrWhiteSpace(request.SessionId))
        {
            return BadRequest(new { error = new { code = "invalid_request", message = "sessionId is required.", hint = (string?)null } });
        }

        var stopped = _sessions.StopSession(request.SessionId);
        if (!stopped)
        {
            return NotFound(new { error = new { code = "not_found", message = "Session not found", hint = (string?)null } });
        }

        return Ok(new { stopped = true });
    }

    private static RunningSessionDto Map(RunningSession session) => new()
    {
        SessionId = session.SessionId,
        GameId = session.GameId,
        EmulatorId = session.EmulatorId,
        StartedAtUtc = session.StartedAtUtc,
        LastHeartbeatUtc = session.LastHeartbeatUtc,
        Status = session.Status
    };
}
#pragma warning restore CA1515
