using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using GameBot.Domain.Logging;
using GameBot.Domain.Queues;
using GameBot.Domain.QueueTemplates;
using GameBot.Domain.Services;
using GameBot.Domain.Sessions;
using GameBot.Service.Services.ExecutionLog;
using GameBot.Service.Services.QueueExecution;
using GameBot.Service.Services.SequenceExecution;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

#pragma warning disable CA2007, CA1861, CA1859, CA1034 // CA1034: nested so that the tests can use the queue harness

namespace GameBot.UnitTests.Queues;

public sealed partial class QueueExecutionServiceTests {
  /// <summary>
  /// Feature 121 (research R-009): after a device recovery rebinds the session, the run loop, the gate
  /// and the watch follow <c>handle.SessionId</c>, and the held firings stay in the loop state.
  /// </summary>
  public sealed class QueueExecutionServiceRebindTests {
    // Records the session of each firing.
    private sealed class SessionRecordingExecution : ISequenceExecutionService {
      private readonly List<(string SequenceId, string? SessionId)> _calls = new();

      public List<(string SequenceId, string? SessionId)> Calls { get { lock (_calls) return _calls.ToList(); } }

      public Task<SequenceExecutionResult> ExecuteAsync(string sequenceId, string? sessionId, ExecutionLogContext? parentContext, CancellationToken ct = default)
        => ExecuteAsync(sequenceId, sessionId, parentContext, GameBot.Domain.Parameters.ParameterScope.Empty, ct: ct);

      public Task<SequenceExecutionResult> ExecuteAsync(string sequenceId, string? sessionId, ExecutionLogContext? parentContext, GameBot.Domain.Parameters.ParameterScope scope, bool dryRun = false, CancellationToken ct = default) {
        lock (_calls) _calls.Add((sequenceId, sessionId));
        var r = SequenceExecutionResult.Start(sequenceId);
        r.Complete();
        return Task.FromResult(r);
      }
    }

    private sealed class Setup {
      public FakeQueueRepository Queues { get; } = new();
      public FakeTemplateRepository Templates { get; } = new();
      public FakeSessionManager Sessions { get; } = new();
      public RecordingExecutionLog Log { get; } = new();
      public QueueRuntimeStore Runtime { get; } = new();
      public QueueRunRegistry Registry { get; } = new();
      public SessionRecordingExecution Execution { get; } = new();
      public FakeSessionLivenessService Liveness { get; } = new() {
        Options = new DeviceLivenessOptions { QueueCheckIntervalMs = 10, QueueGracePeriodMs = 120000 }
      };
      public QueueExecutionService Service { get; }
      public ExecutionQueue Queue { get; }

      public Setup(QueueTemplateEntry entry, bool withLiveness = false) {
        var template = new QueueTemplate { Id = "tpl-q1", Name = "T-q1" };
        template.Entries.Add(entry);
        Templates.Add(template);
        Queue = new ExecutionQueue { Id = "q1", Name = "Q-q1", EmulatorSerial = "emu-1", LinkedTemplateId = template.Id };
        Queues.Add(Queue);
        Service = new QueueExecutionService(Queues, Runtime, Templates, Execution, Sessions, Log,
          NullLogger<QueueExecutionService>.Instance, Registry, liveness: withLiveness ? Liveness : null);
      }

      public QueueRunHandle Handle {
        get {
          Registry.TryGet("q1", out var handle).Should().BeTrue();
          return handle;
        }
      }
    }

    [Fact]
    public async Task RebindSession_StopsTheOldSessionAndSetsTheNewOneOnTheHandle() {
      var s = new Setup(new QueueTemplateEntry { SequenceId = "A", ScheduleType = ScheduleType.Timer, TimerRelativeOffset = TimeSpan.FromHours(1) });
      await s.Service.StartAsync("q1");
      await WaitForAsync(() => s.Registry.TryGet("q1", out var h) && h.SessionId is not null);
      var handle = s.Handle;
      var oldId = handle.SessionId!;

      await s.Service.RebindSessionAsync(s.Queue, handle);

      handle.SessionId.Should().NotBe(oldId);
      s.Sessions.Stopped.Should().Contain(oldId);
      s.Sessions.GetSession(handle.SessionId!).Should().NotBeNull();
      await s.Service.StopAsync("q1");
      await WaitUntilStoppedAsync(s.Service, "q1");
    }

    [Fact]
    public async Task TheRunLoop_FollowsTheHandleSessionForTheNextFiring() {
      var s = new Setup(new QueueTemplateEntry { SequenceId = "A", ScheduleType = ScheduleType.Timer, TimerRelativeOffset = TimeSpan.FromHours(1) });
      await s.Service.StartAsync("q1");
      await WaitForAsync(() => s.Registry.TryGet("q1", out var h) && h.SessionId is not null);
      var handle = s.Handle;
      await s.Service.RebindSessionAsync(s.Queue, handle);
      var newId = handle.SessionId!;

      s.Service.ScheduleRelative("q1", "B", TimeSpan.Zero);
      await WaitForAsync(() => s.Execution.Calls.Any(c => c.SequenceId == "B"));

      s.Execution.Calls.Single(c => c.SequenceId == "B").SessionId.Should().Be(newId);
      await s.Service.StopAsync("q1");
      await WaitUntilStoppedAsync(s.Service, "q1");
    }

    [Fact]
    public async Task AHeldFiring_RunsOnTheNewSessionAfterTheRebind() {
      var s = new Setup(new QueueTemplateEntry { SequenceId = "A", ScheduleType = ScheduleType.OncePerRun }, withLiveness: true);
      s.Liveness.SetNotLive(DeviceLivenessReasons.CaptureStalled);
      await s.Service.StartAsync("q1");
      await WaitForAsync(() => s.Registry.TryGet("q1", out var h) && h.Liveness.Snapshot().GatedFirings > 0);
      s.Execution.Calls.Should().BeEmpty("the gate holds the firing");
      var handle = s.Handle;

      await s.Service.RebindSessionAsync(s.Queue, handle);
      s.Liveness.SetLive();
      await WaitForAsync(() => s.Execution.Calls.Count > 0);

      s.Execution.Calls.Should().ContainSingle().Which.SessionId.Should().Be(handle.SessionId);
      await s.Service.StopAsync("q1");
      await WaitUntilStoppedAsync(s.Service, "q1");
    }

    [Fact]
    public async Task TheRunEnd_StopsTheReboundSession() {
      var s = new Setup(new QueueTemplateEntry { SequenceId = "A", ScheduleType = ScheduleType.Timer, TimerRelativeOffset = TimeSpan.FromHours(1) });
      await s.Service.StartAsync("q1");
      await WaitForAsync(() => s.Registry.TryGet("q1", out var h) && h.SessionId is not null);
      var handle = s.Handle;
      await s.Service.RebindSessionAsync(s.Queue, handle);
      var newId = handle.SessionId!;

      await s.Service.StopAsync("q1");
      await WaitUntilStoppedAsync(s.Service, "q1");

      s.Sessions.Stopped.Should().Contain(newId);
    }
  }
}
