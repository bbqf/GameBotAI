using System;
using System.Linq;
using System.Threading;
using FluentAssertions;
using GameBot.Domain.Commands.SelfReschedule;
using GameBot.Service.Services.QueueExecution;
using Xunit;

namespace GameBot.IntegrationTests.Queues;

/// <summary>
/// Feature 125: a booking that exists before a run (a held and re-armed booking) does not block the
/// first <c>keep: earliest</c> booking of the next run. The real hold path drains a due booking from the
/// run register and puts it back with the same call that the queue engine uses. The engine has no other
/// code that writes a Timer booking, so this is the real path of a booking with no run id.
/// </summary>
public sealed class SelfRescheduleKeepRetryIntegrationTests {
  [Fact]
  public void AReArmedBookingIsReplacedByTheFirstBookingOfTheNextRun() {
    var registry = new QueueRunRegistry();
    var handle = new QueueRunHandle { QueueId = "q1", Cts = new CancellationTokenSource() };
    registry.TryAdd("q1", handle);
    var coordinator = new SelfRescheduleCoordinator(registry);

    // A booking of an earlier run is due, the engine drains it, a liveness hold re-arms it.
    coordinator.ScheduleSelf("q1", "seq-A", SelfRescheduleOption.Timer, null, TimeSpan.FromSeconds(-1), null, SelfRescheduleKeep.Earliest, "run-old");
    var drained = handle.DrainDueTimerFirings(DateTimeOffset.Now).Should().ContainSingle().Subject;
    handle.RearmTimerFiring(drained);
    handle.SnapshotPendingTimerFirings().Should().ContainSingle().Which.RunId.Should().BeNull();

    // The next run books 20 minutes, then 10 minutes, then 30 minutes.
    var first = coordinator.ScheduleSelf("q1", "seq-A", SelfRescheduleOption.Timer, null, TimeSpan.FromMinutes(20), null, SelfRescheduleKeep.Earliest, "run-new");
    first.KeptPending.Should().BeFalse();
    var second = coordinator.ScheduleSelf("q1", "seq-A", SelfRescheduleOption.Timer, null, TimeSpan.FromMinutes(10), null, SelfRescheduleKeep.Earliest, "run-new");
    second.KeptPending.Should().BeFalse();
    var third = coordinator.ScheduleSelf("q1", "seq-A", SelfRescheduleOption.Timer, null, TimeSpan.FromMinutes(30), null, SelfRescheduleKeep.Earliest, "run-new");
    third.KeptPending.Should().BeTrue();

    var pending = handle.SnapshotPendingTimerFirings().Should().ContainSingle().Subject;
    pending.RunId.Should().Be("run-new");
    (pending.FireAt!.Value - DateTimeOffset.Now).TotalMinutes.Should().BeInRange(9, 10.2);
  }
}
