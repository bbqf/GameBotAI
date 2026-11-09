using System;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using GameBot.Domain.Queues;
using GameBot.Domain.QueueTemplates;
using GameBot.Domain.Sessions;
using GameBot.Service.Services.QueueExecution;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

#pragma warning disable CA2007, CA1861, CA1859, CA1034, CA2000 // CA1034: nested so that the tests can use the queue harness

namespace GameBot.UnitTests.Queues;

public sealed partial class QueueExecutionServiceTests {
  /// <summary>
  /// Feature 184: a run-next request meets the device-liveness gate of feature 106. A held request stays queued, keeps its
  /// place, and runs once after the device is live again.
  /// </summary>
  public sealed class QueueRunNextLivenessTests {
    private static readonly DateTimeOffset Noon = new(2026, 9, 25, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task AHeldRequestStaysQueuedAndRunsOnceAfterARecovery() {
      var clock = new FakeTimeProvider(Noon);
      var h = new Harness(clock);
      var liveness = new FakeSessionLivenessService();
      var service = new QueueExecutionService(h.Queues, h.Runtime, h.Templates, h.Sequences, h.Sessions, h.Log,
        NullLogger<QueueExecutionService>.Instance, h.Registry, timeProvider: clock, liveness: liveness);
      AddQueueWithEntries(h, "q1", new[] { WithId(OncePerRun("A"), "e-a"), WithId(RelativeTimer("Far", TimeSpan.FromHours(1)), "e-far") });

      await service.StartAsync("q1");
      await WaitForAsync(() => h.Sequences.Executed.Contains("A"));
      var handle = await WaitForHandleAsync(h, "q1");
      await WaitForAsync(() => handle.Schedule is not null && StateOf(h, handle, "e-a") == "idle");

      liveness.SetNotLive(DeviceLivenessReasons.CaptureStalled);
      service.RunNext("q1", "e-a").Outcome.Should().Be(RunNextOutcome.Queued);
      var target = liveness.Evaluations + 6;
      await WaitForAsync(() => liveness.Evaluations >= target, 5000);

      h.Sequences.Executed.Count(id => id == "A").Should().Be(1, "a held request does not run");
      handle.IsRunNextQueued("e-a").Should().BeTrue("a held request goes back to the register");
      handle.SnapshotRunNext().Should().ContainSingle();

      liveness.SetLive();
      await WaitForAsync(() => h.Sequences.Executed.Count(id => id == "A") >= 2);
      await Task.Delay(300);

      h.Sequences.Executed.Count(id => id == "A").Should().Be(2, "the request runs once");
      handle.HasPendingRunNext.Should().BeFalse();
      await service.StopAsync("q1");
      await WaitUntilStoppedAsync(service, "q1");
    }
  }
}
