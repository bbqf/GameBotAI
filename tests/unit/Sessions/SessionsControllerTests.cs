using System;
using System.Collections.Generic;
using FluentAssertions;
using GameBot.Domain.Sessions;
using GameBot.Service.Controllers;
using GameBot.Service.Models;
using GameBot.Service.Services;
using Microsoft.AspNetCore.Mvc;
using System.Threading;
using Xunit;

namespace GameBot.UnitTests.Sessions;

public sealed class SessionsControllerTests {
  [Fact]
  public void GetRunningSessions_MapsFromService() {
    var running = new RunningSession {
      SessionId = "sess-1",
      GameId = "game-1",
      EmulatorId = "emu-1",
      StartedAtUtc = new DateTime(2024, 01, 02, 03, 04, 05, DateTimeKind.Utc),
      LastHeartbeatUtc = new DateTime(2024, 01, 02, 03, 10, 00, DateTimeKind.Utc),
      Status = RunningSessionStatus.Stopping
    };
    var service = new FakeSessionService { Running = new[] { running } };
    var controller = new SessionsController(service);

    var result = controller.GetRunningSessions();

    var ok = Assert.IsType<OkObjectResult>(result.Result);
    var payload = Assert.IsAssignableFrom<RunningSessionsResponse>(ok.Value);
    payload.Sessions.Should().ContainSingle();
    payload.Sessions.Should().ContainEquivalentOf(new RunningSessionDto {
      SessionId = "sess-1",
      GameId = "game-1",
      EmulatorId = "emu-1",
      StartedAtUtc = running.StartedAtUtc,
      LastHeartbeatUtc = running.LastHeartbeatUtc,
      Status = RunningSessionStatus.Stopping
    });
  }

  [Fact]
  public void StopSession_ReturnsNotFoundWhenServiceFails() {
    var service = new FakeSessionService { StopResult = false };
    var controller = new SessionsController(service);

    var result = controller.StopSession(new StopSessionRequest { SessionId = "missing" });

    var notFound = Assert.IsType<NotFoundObjectResult>(result);
    var error = GetError(notFound.Value);
    error.code.Should().Be("not_found");
  }

  [Fact]
  public void StartSession_Returns429OnCapacityExceeded() {
    var service = new FakeSessionService { ThrowOnStart = new InvalidOperationException("capacity_exceeded") };
    var controller = new SessionsController(service);

    var result = controller.StartSession(new StartSessionRequest { GameId = "g1", EmulatorId = "emu-1" });

    var tooMany = Assert.IsType<ObjectResult>(result.Result);
    tooMany.StatusCode.Should().Be(429);
    var error = GetError(tooMany.Value);
    error.code.Should().Be("capacity_exceeded");
  }

  private static (string code, string? message) GetError(object? value) {
    value.Should().NotBeNull();
    var errorProp = value!.GetType().GetProperty("error")?.GetValue(value);
    errorProp.Should().NotBeNull();
    var code = errorProp!.GetType().GetProperty("code")?.GetValue(errorProp) as string;
    var message = errorProp!.GetType().GetProperty("message")?.GetValue(errorProp) as string;
    return (code ?? string.Empty, message);
  }
}

file sealed class FakeSessionService : ISessionService {
  public IReadOnlyCollection<RunningSession>? Running { get; set; }
  public bool StopResult { get; set; } = true;
  public Exception? ThrowOnStart { get; set; }
  public string? LastStopId { get; private set; }

  public IReadOnlyCollection<RunningSession> GetRunningSessions() => Running ?? Array.Empty<RunningSession>();

  public RunningSession StartSession(string gameId, string emulatorId, CancellationToken ct = default) {
    if (ThrowOnStart is not null) throw ThrowOnStart;
    var session = new RunningSession {
      SessionId = "started",
      GameId = gameId,
      EmulatorId = emulatorId,
      StartedAtUtc = DateTime.UtcNow,
      LastHeartbeatUtc = DateTime.UtcNow,
      Status = RunningSessionStatus.Running
    };
    Running = new[] { session };
    return session;
  }

  public bool StopSession(string sessionId) {
    LastStopId = sessionId;
    return StopResult;
  }
}
