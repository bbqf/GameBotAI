using GameBot.Domain.Sessions;
using GameBot.Emulator.Session;
using GameBot.Service;
using GameBot.Service.Models;
using GameBot.Emulator.Adb;
using Microsoft.Extensions.Options;
using System.Threading.Tasks;
using GameBot.Service.Services;
using GameBot.Service.Services.Liveness;
using Microsoft.Extensions.Logging;

namespace GameBot.Service.Endpoints;

internal static class SessionsEndpoints {
  public static IEndpointRouteBuilder MapSessionEndpoints(this IEndpointRouteBuilder app) {
    var group = app.MapGroup(ApiRoutes.Sessions).WithTags("Sessions");

    group.MapPost("", async (CreateSessionRequest req, ISessionManager mgr, ISessionContextCache cache, IOptions<SessionCreationOptions> createOptions, ILogger<SessionsLoggingTag> logger, CancellationToken ct) => {
      var game = req.GameId ?? req.GamePath;
      if (string.IsNullOrWhiteSpace(game))
        return Results.BadRequest(new { error = new { code = "invalid_request", message = "Provide gameId or gamePath.", hint = (string?)null } });

      if (!mgr.CanCreateSession) {
        return Results.Json(new { error = new { code = "capacity_exceeded", message = "Max concurrent sessions reached.", hint = (string?)null } }, statusCode: StatusCodes.Status429TooManyRequests);
      }
      var timeoutSeconds = Math.Max(1, createOptions?.Value?.TimeoutSeconds ?? 30);
      using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
      timeoutCts.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));

      try {
        var createTask = Task.Run(() => mgr.CreateSession(game, req.AdbSerial), timeoutCts.Token);
        var sess = await createTask.WaitAsync(TimeSpan.FromSeconds(timeoutSeconds), timeoutCts.Token).ConfigureAwait(false);
        cache.SetSessionId(sess.GameId, sess.DeviceSerial ?? req.AdbSerial ?? string.Empty, sess.Id);
        SessionsLog.SessionCreated(logger, sess.Id, sess.GameId, sess.DeviceSerial ?? req.AdbSerial ?? string.Empty);
        var resp = new CreateSessionResponse { Id = sess.Id, SessionId = sess.Id, Status = sess.Status.ToString().ToUpperInvariant(), GameId = sess.GameId };
        return Results.Created($"{ApiRoutes.Sessions}/{sess.Id}", resp);
      }
      catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested && !ct.IsCancellationRequested) {
        return Results.StatusCode(StatusCodes.Status504GatewayTimeout);
      }
      catch (TimeoutException) {
        return Results.StatusCode(StatusCodes.Status504GatewayTimeout);
      }
      catch (InvalidOperationException ex) when (ex.Message == "no_adb_devices") {
        return Results.NotFound(new { error = new { code = "adb_device_not_found", message = "No ADB devices connected.", hint = "Connect a device or emulator and try again." } });
      }
      catch (KeyNotFoundException ex) {
        return Results.NotFound(new { error = new { code = "adb_device_not_found", message = ex.Message, hint = "Check /adb/devices for available serials." } });
      }
    }).WithName("CreateSession").WithTags("Sessions");

    group.MapGet("{id}", (string id, ISessionManager mgr) => {
      var s = mgr.GetSession(id);
      return s is null
          ? Results.NotFound(new { error = new { code = "not_found", message = "Session not found", hint = (string?)null } })
          : Results.Ok(new { id = s.Id, status = s.Status.ToString().ToUpperInvariant(), uptime = (long)s.Uptime.TotalSeconds, health = s.Health.ToString().ToUpperInvariant(), gameId = s.GameId });
    }).WithName("GetSession").WithTags("Sessions");

    // Surface chosen device for this session (if ADB mode bound one)
    group.MapGet("{id}/device", (string id, ISessionManager mgr) => {
      var s = mgr.GetSession(id);
      return s is null
          ? Results.NotFound(new { error = new { code = "not_found", message = "Session not found", hint = (string?)null } })
          : Results.Ok(new {
            id = s.Id,
            deviceSerial = s.DeviceSerial,
            mode = string.IsNullOrWhiteSpace(s.DeviceSerial) ? "STUB" : "ADB"
          });
    }).WithName("GetSessionDevice").WithTags("Sessions");

    group.MapPost("{id}/inputs", async (string id, InputActionsRequest req, ISessionManager mgr, ISessionLivenessService liveness, CancellationToken ct) => {
      if (req.Actions is null || req.Actions.Count == 0)
        return Results.BadRequest(new { error = new { code = "invalid_request", message = "No actions provided.", hint = (string?)null } });

      // B-001: a session that IS running but posted actions that can't be parsed/dispatched used
      // to be misreported as "not_running" (409) — the two failure modes are now distinguished by
      // checking whether the session was found at all, rather than by whether anything dispatched.
      var dispatch = await mgr.SendInputsWithResultsAsync(id, req.Actions.Select(a => new GameBot.Emulator.Session.InputAction(a.Type, a.Args, a.DelayMs, a.DurationMs)), ct).ConfigureAwait(false);
      // Feature 106 (FR-013): the data-only liveness report, after the dispatch. No probe, so a live
      // device gets no extra delay.
      var session = dispatch.SessionFound ? mgr.GetSession(id) : null;
      var report = session is null ? null : liveness.Evaluate(session);
      return MapDispatchResult(dispatch, report, id, liveness.Options.InputTimeoutMs);
    }).WithName("SendInputs").WithTags("Sessions");

    // Session health endpoint: the ADB transport, and the device liveness (feature 106). The probe is
    // bounded: at most TransportCheckTimeoutMs + CaptureTimeoutMs.
    group.MapGet("{id}/health", async (string id, ISessionManager mgr, ISessionLivenessService liveness, CancellationToken ct) => {
      var s = mgr.GetSession(id);
      if (s is null)
        return Results.NotFound(new { error = new { code = "not_found", message = "Session not found", hint = (string?)null } });

      var probe = await liveness.ProbeAsync(s, ct).ConfigureAwait(false);
      var livenessBlock = BuildLivenessBlock(probe.Liveness);
      if (probe.Adb is { } adb) {
        object adbBlock = adb.Error is not null
          ? new { ok = false, error = adb.Error }
          : new { ok = adb.Ok, stdout = adb.Stdout, stderr = adb.Stderr };
        return Results.Ok(new { id = s.Id, mode = "ADB", deviceSerial = s.DeviceSerial, adb = adbBlock, liveness = livenessBlock });
      }

      return Results.Ok(new { id = s.Id, mode = "STUB", deviceSerial = (string?)null, adb = new { ok = true }, liveness = livenessBlock });
    }).WithName("GetSessionHealth").WithTags("Sessions");

    group.MapGet("{id}/snapshot", async (string id, HttpContext ctx, ISessionManager mgr, ISessionLivenessService liveness, IDeviceLivenessTracker tracker, CancellationToken ct) => {
      // Feature 106 (FR-011): the direct capture gets the limit CaptureTimeoutMs. A cancel by the
      // client (ct) is not a 504 and keeps its current behavior.
      var limitMs = liveness.Options.CaptureTimeoutMs;
      using var limit = CancellationTokenSource.CreateLinkedTokenSource(ct);
      limit.CancelAfter(limitMs);
      try {
        var png = await mgr.GetSnapshotAsync(id, limit.Token).WaitAsync(limit.Token).ConfigureAwait(false);
        ApplySnapshotHeaders(ctx.Response, mgr, liveness, tracker, id);
        return Results.File(png, contentType: "image/png");
      }
      catch (KeyNotFoundException) {
        return Results.NotFound(new { error = new { code = "not_found", message = "Session not found", hint = (string?)null } });
      }
      catch (OperationCanceledException) when (limit.IsCancellationRequested && !ct.IsCancellationRequested) {
        return Results.Json(new {
          error = new {
            code = "capture_timeout",
            message = string.Format(System.Globalization.CultureInfo.InvariantCulture, "The device did not return a screenshot in {0} ms.", limitMs),
            hint = "Check the emulator, or restart it."
          }
        }, statusCode: StatusCodes.Status504GatewayTimeout);
      }
    }).WithName("GetSnapshot").WithTags("Sessions");

    group.MapDelete("{id}", (string id, ISessionManager mgr) => {
      var stopped = mgr.StopSession(id);
      return stopped ? Results.Accepted($"{ApiRoutes.Sessions}/{id}", new { status = "stopping" })
                     : Results.NotFound(new { error = new { code = "not_found", message = "Session not found", hint = (string?)null } });
    }).WithName("StopSession").WithTags("Sessions");

    return app;
  }

  /// <summary>
  /// Feature 106 (FR-012, FR-013, contract <c>session-inputs.md</c>): the answer of the inputs endpoint
  /// after the dispatch. The rule order is 409 <c>not_running</c>, 504 <c>device_timeout</c>,
  /// 503 <c>device_not_live</c>, 400 <c>invalid_input_actions</c>, 202. The 504 rule keeps the
  /// <c>dispatched</c> values of the actions before the timed-out action. The 503 rule reports each
  /// result as not dispatched, because the device does not apply the inputs.
  /// </summary>
  internal static IResult MapDispatchResult(SessionInputDispatchResult dispatch, DeviceLivenessReport? report, string sessionId, int inputTimeoutMs) {
    if (!dispatch.SessionFound) {
      return Results.Conflict(new { error = new { code = "not_running", message = "Session not running.", hint = (string?)null } });
    }

    var timedOut = dispatch.Results.FirstOrDefault(r => r.TimedOut);
    if (timedOut is not null) {
      var message = string.Format(System.Globalization.CultureInfo.InvariantCulture,
        "The device did not answer action {0} in {1} ms. The actions after it were not sent.", timedOut.Index, inputTimeoutMs);
      return Results.Json(new {
        error = new {
          code = "device_timeout",
          message,
          hint = $"Get GET {ApiRoutes.Sessions}/{sessionId}/health to see the device liveness."
        },
        results = ToDto(dispatch.Results)
      }, statusCode: StatusCodes.Status504GatewayTimeout);
    }

    var accepted = dispatch.Results.Count(r => r.Dispatched);
    if (accepted > 0 && report is { State: DeviceLivenessStates.NotLive } notLive) {
      var reason = notLive.Reason ?? "unknown";
      var rewritten = dispatch.Results.Select(r => r.Dispatched
        ? new { index = r.Index, dispatched = false, failureReason = (string?)$"device_not_live: {reason}" }
        : new { index = r.Index, dispatched = false, failureReason = r.FailureReason }).ToArray();
      return Results.Json(new {
        error = new {
          code = "device_not_live",
          reason,
          message = $"The device is not live ({reason}). The inputs were sent, but the device does not apply them.",
          hint = $"Get GET {ApiRoutes.Sessions}/{sessionId}/health for details. Restart the emulator if the fault stays."
        },
        results = rewritten
      }, statusCode: StatusCodes.Status503ServiceUnavailable);
    }

    var resultsDto = ToDto(dispatch.Results);
    if (accepted == 0) {
      return Results.Json(
        new { error = new { code = "invalid_input_actions", message = "No posted actions could be dispatched.", hint = (string?)null }, results = resultsDto },
        statusCode: StatusCodes.Status400BadRequest);
    }

    return Results.Accepted($"{ApiRoutes.Sessions}/{sessionId}", new { accepted, results = resultsDto });
  }

  private static object[] ToDto(IReadOnlyList<InputActionResult> results) =>
    results.Select(r => (object)new { index = r.Index, dispatched = r.Dispatched, failureReason = r.FailureReason }).ToArray();

  /// <summary>
  /// Feature 106 (FR-010): the staleness headers of a snapshot. The snapshot is always a direct capture,
  /// so the headers come only when a capture loop runs, or ran, for the session.
  /// </summary>
  private static void ApplySnapshotHeaders(HttpResponse response, ISessionManager mgr, ISessionLivenessService liveness, IDeviceLivenessTracker tracker, string id) {
    if (!tracker.HasCaptureData(id)) return;
    var session = mgr.GetSession(id);
    if (session is null) return;
    CaptureHeaders.Apply(response, CaptureHeaders.FromReport(liveness.Evaluate(session), directCapture: true));
  }

  /// <summary>The <c>liveness</c> block of the session health response (feature 106, contract <c>session-health.md</c>).</summary>
  internal static object BuildLivenessBlock(DeviceLivenessReport report) => new {
    state = report.State,
    reason = report.Reason,
    frameAgeMs = report.FrameAgeMs,
    unchangedMs = report.UnchangedMs,
    stale = report.Stale,
    lastInputAt = report.LastInputAt,
    lastInputOutcome = report.LastInputOutcome
  };
}

internal static partial class SessionsLog {
  [LoggerMessage(EventId = 7000, Level = LogLevel.Information, Message = "Session created {SessionId} for game {GameId} adb {AdbSerial}.")]
  public static partial void SessionCreated(ILogger logger, string SessionId, string GameId, string AdbSerial);
}

internal sealed class SessionsLoggingTag { }
