using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using GameBot.Domain.Queues;
using GameBot.Domain.Sessions;
using GameBot.Service.Services.QueueExecution;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

#pragma warning disable CA2007, CA2000, CA1034 // CA1034: nested so that the tests can use the queue harness

namespace GameBot.UnitTests.Queues;

public sealed partial class QueueExecutionServiceTests {
  /// <summary>
  /// Feature 201 (research R-003): <c>TryBindLostSession</c> is the one place that binds a session again for a
  /// lost session. It binds one session only, it does not bind while a recovery attempt runs, and a failed bind
  /// leaves no session ID.
  /// </summary>
  public sealed class QueueLostSessionTests {
    private sealed class Rig {
      public FakeSessionManager Sessions { get; } = new();
      public FakeSessionLivenessService Liveness { get; } = new();
      public QueueExecutionService Service { get; }
      public ExecutionQueue Queue { get; } = new() { Id = "q1", Name = "Q-q1", EmulatorSerial = "emu-1" };
      public QueueRunHandle Handle { get; } = new() { QueueId = "q1", Cts = new CancellationTokenSource() };

      public Rig() {
        Service = new QueueExecutionService(new FakeQueueRepository(), new QueueRuntimeStore(), new FakeTemplateRepository(),
          new FakeSequenceExecution(), Sessions, new RecordingExecutionLog(), NullLogger<QueueExecutionService>.Instance,
          new QueueRunRegistry(), liveness: Liveness);
      }
    }

    [Fact]
    public void ABoundSessionThatExistsIsKeptAndNothingIsBound() {
      var rig = new Rig();
      var session = rig.Sessions.CreateSession("queue:q1", "emu-1");
      rig.Handle.SessionId = session.Id;

      var id = rig.Service.TryBindLostSession(rig.Queue, rig.Handle, out var error);

      id.Should().Be(session.Id);
      error.Should().BeNull();
      rig.Sessions.Created.Should().Be(1);
    }

    [Fact]
    public void ALostSessionAndAListedDeviceBindOneNewSession() {
      var rig = new Rig();
      rig.Handle.SessionId = "gone";
      rig.Handle.LostSessionWarned = true;

      var id = rig.Service.TryBindLostSession(rig.Queue, rig.Handle, out var error);

      id.Should().NotBeNull();
      error.Should().BeNull();
      rig.Handle.SessionId.Should().Be(id);
      rig.Handle.LostSessionWarned.Should().BeFalse();
      rig.Sessions.Created.Should().Be(1);
      rig.Sessions.GetSession(id!)!.OwnerQueueId.Should().Be("q1");
    }

    [Fact]
    public void ALostSessionAndADeviceThatIsNotListedGiveNoSession() {
      var rig = new Rig();
      rig.Handle.SessionId = "gone";
      rig.Sessions.CreateThrows = new KeyNotFoundException("ADB device 'emu-1' not found");

      var id = rig.Service.TryBindLostSession(rig.Queue, rig.Handle, out var error);
      var again = rig.Service.TryBindLostSession(rig.Queue, rig.Handle, out _);

      id.Should().BeNull();
      again.Should().BeNull();
      error.Should().BeOfType<KeyNotFoundException>();
      rig.Handle.SessionId.Should().BeNull();
      rig.Handle.LostSessionWarned.Should().BeTrue();
    }

    [Fact]
    public void NoBindWhileARecoveryAttemptRuns() {
      var rig = new Rig();
      rig.Handle.SessionId = null;
      var t0 = DateTimeOffset.UtcNow;
      rig.Handle.Liveness.Observe(QueueExecutionService.NoSessionReport, t0);
      rig.Handle.Liveness.TryBeginRecovery(t0.AddSeconds(2), TimeSpan.FromSeconds(1), 2, TimeSpan.Zero).Should().BeTrue();

      var id = rig.Service.TryBindLostSession(rig.Queue, rig.Handle, out var error);

      id.Should().BeNull();
      error.Should().BeNull();
      rig.Sessions.Created.Should().Be(0, "the recovery runner owns the rebind while an attempt runs");
    }

    [Fact]
    public async Task TwoParallelCallsBindOnlyOneSession() {
      var rig = new Rig();
      rig.Handle.SessionId = "gone";
      using var start = new Barrier(2);

      string? Bind() {
        start.SignalAndWait();
        return rig.Service.TryBindLostSession(rig.Queue, rig.Handle, out _);
      }

      var results = await Task.WhenAll(Task.Run(Bind), Task.Run(Bind));

      rig.Sessions.Created.Should().Be(1);
      results.Distinct().Should().ContainSingle().Which.Should().Be(rig.Handle.SessionId);
    }

    [Fact]
    public void TheNoSessionReportIsAHardNotLiveReport() {
      var report = QueueExecutionService.NoSessionReport;

      report.State.Should().Be(DeviceLivenessStates.NotLive);
      report.Reason.Should().Be(DeviceLivenessReasons.TransportNotReady);
      report.IsHardNotLive.Should().BeTrue();
    }
  }
}
