using GameBot.Domain.Queues;
using GameBot.Domain.Sessions;
using GameBot.Emulator.Session;
using GameBot.Service.Endpoints.Dto;
using GameBot.Service.Services.Liveness;
using GameBot.Service.Services.Monitoring;

namespace GameBot.Service.Endpoints;

/// <summary>
/// Feature 141: the monitoring routes. The list route is a literal segment, so it wins over
/// <c>GET /api/sessions/{id}</c> (FR-021). The logic is in the services, so this file stays thin.
/// </summary>
internal static class SessionMonitoringEndpoints {
  public static IEndpointRouteBuilder MapSessionMonitoringEndpoints(this IEndpointRouteBuilder app) {
    var group = app.MapGroup(ApiRoutes.Sessions).WithTags("Sessions");

    group.MapGet("monitoring", ListAsync)
      .WithName("ListMonitoredSessions")
      .Produces<SessionMonitoringListResponse>(StatusCodes.Status200OK);

    group.MapPost("{id}/restart", StartRestartAsync)
      .WithName("RestartSessionEmulator")
      .Produces<SessionRestartRecordDto>(StatusCodes.Status202Accepted);

    group.MapGet("{id}/restart", GetRestart)
      .WithName("GetSessionRestart")
      .Produces<SessionRestartRecordDto>(StatusCodes.Status200OK);

    return app;
  }

  private static async Task<IResult> ListAsync(
      ISessionManager sessions, ISessionLivenessService liveness, ISessionRestartService restarts, IQueueRepository queues) {
    var list = sessions.ListSessions();
    restarts.Prune(list.Select(s => s.Id).ToList());
    var queueNames = (await queues.ListAsync().ConfigureAwait(false)).ToDictionary(q => q.Id, q => q.Name, StringComparer.Ordinal);
    var items = list
      .OrderBy(s => s.StartTime)
      .Select(s => ToDto(s, liveness, restarts, queueNames))
      .ToList();
    return Results.Ok(new SessionMonitoringListResponse { Sessions = items });
  }

  private static MonitoredSessionDto ToDto(
      EmulatorSession s, ISessionLivenessService liveness, ISessionRestartService restarts, Dictionary<string, string> queueNames) {
    var availability = restarts.GetAvailability(s.DeviceSerial);
    var (state, reason) = ResolveState(s, liveness, restarts);
    string? queueName = null;
    if (s.OwnerQueueId is { } queueId) queueNames.TryGetValue(queueId, out queueName);
    return new MonitoredSessionDto {
      Id = s.Id,
      Name = string.IsNullOrWhiteSpace(s.DeviceSerial) ? s.GameId : $"{s.GameId} ({s.DeviceSerial})",
      GameId = s.GameId,
      DeviceSerial = s.DeviceSerial,
      State = state,
      Reason = reason,
      OwnerQueueId = s.OwnerQueueId,
      OwnerQueueName = queueName,
      RestartAvailable = availability.Available,
      RestartUnavailableReason = availability.Reason,
      StartedAtUtc = s.StartTime
    };
  }

  /// <summary>The state rules of data-model.md. The first matching rule wins. No device probe.</summary>
  private static (string State, string? Reason) ResolveState(EmulatorSession s, ISessionLivenessService liveness, ISessionRestartService restarts) {
    if (restarts.Get(s.Id) is { IsRunning: true }) return (MonitoredSessionStates.Restarting, null);
    var report = liveness.Evaluate(s);
    if (report.State == DeviceLivenessStates.NotLive) return (MonitoredSessionStates.NoAnswer, report.Reason);
    if (s.Status == SessionStatus.Stopping) return (MonitoredSessionStates.Stopping, null);
    return (MonitoredSessionStates.Live, null);
  }

  private static async Task<IResult> StartRestartAsync(string id, ISessionManager sessions, ISessionRestartService restarts, CancellationToken ct) {
    var session = sessions.GetSession(id);
    if (session is null) return SessionNotFound();

    var result = await restarts.StartAsync(session, ct).ConfigureAwait(false);
    return result.Status switch {
      RestartStartStatus.Accepted => Results.Json(result.Record!.ToDto(), statusCode: StatusCodes.Status202Accepted),
      RestartStartStatus.InProgress => Results.Json(new {
        error = new {
          code = "restart_in_progress",
          message = "A restart is already running for this emulator.",
          hint = $"Get GET {ApiRoutes.Sessions}/{id}/restart to see the progress."
        }
      }, statusCode: StatusCodes.Status409Conflict),
      _ => RestartUnavailable(result.UnavailableReason)
    };
  }

  private static IResult RestartUnavailable(string? reason) =>
    Results.Json(new {
      error = new {
        code = "restart_unavailable",
        reason,
        message = $"The system cannot restart this emulator ({reason}). The emulator did not change.",
        hint = "Use an LDPlayer emulator with a serial such as emulator-5558, and check that ldconsole is installed."
      },
      reason
    }, statusCode: StatusCodes.Status422UnprocessableEntity);

  private static IResult GetRestart(string id, ISessionManager sessions, ISessionRestartService restarts) {
    if (sessions.GetSession(id) is null) return SessionNotFound();
    var record = restarts.Get(id);
    return record is null
      ? Results.NotFound(new { error = new { code = "no_restart", message = "No restart was made for this session.", hint = (string?)null } })
      : Results.Ok(record.ToDto());
  }

  private static IResult SessionNotFound() =>
    Results.NotFound(new { error = new { code = "not_found", message = "Session not found", hint = (string?)null } });
}
