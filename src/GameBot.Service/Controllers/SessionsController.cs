// Suppress CA1515: controllers must remain public for ASP.NET discovery
#pragma warning disable CA1515
using GameBot.Domain.Sessions;
using GameBot.Service.Models;
using Microsoft.AspNetCore.Mvc;

namespace GameBot.Service.Controllers;

[ApiController]
[Route("api/sessions")]
public sealed class SessionsController : ControllerBase
{
    [HttpGet("running")]
    public ActionResult<RunningSessionsResponse> GetRunningSessions()
    {
        // TODO: wire to session tracking service
        return Ok(new RunningSessionsResponse { Sessions = Array.Empty<RunningSessionDto>() });
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

        // TODO: implement start logic with auto-stop replacement
        return Ok(new StartSessionResponse
        {
            SessionId = "",
            RunningSessions = Array.Empty<RunningSessionDto>()
        });
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

        // TODO: implement stop logic
        return Ok(new { stopped = true });
    }
}
#pragma warning restore CA1515
