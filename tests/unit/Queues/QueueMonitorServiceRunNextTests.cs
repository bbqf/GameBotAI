using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using GameBot.Domain.Parameters;
using GameBot.Domain.QueueTemplates;
using GameBot.Service.Services.QueueExecution;
using Xunit;

#pragma warning disable CA2007, CA1861, CA1859

namespace GameBot.UnitTests.Queues;

// Feature 184: the entry states and the queued request in the monitor projection.
public sealed partial class QueueMonitorServiceTests {
  [Fact]
  public async Task EntryStatesAreEmptyWhenTheQueueDoesNotRun() {
    var h = new Harness();
    h.AddQueue();
    h.Entry("A", "Alpha");
    h.Template.Entries[0].EntryId = "e-a";

    var snap = await h.Service.BuildAsync("q1");

    snap.Running.Should().BeFalse();
    (snap.EntryStates ?? new System.Collections.Generic.Dictionary<string, string>()).Should().BeEmpty();
  }

  [Fact]
  public async Task EntryStatesGiveTheStateOfEachEnabledEntryWithAnId() {
    var h = new Harness();
    h.AddQueue();
    h.Entry("A", "Alpha");
    h.Entry("B", "Bravo");
    h.Entry("D", "Delta");
    h.Template.Entries[0].EntryId = "e-a";
    h.Template.Entries[1].EntryId = "e-b";
    h.Template.Entries[2].EntryId = "e-d";
    h.Template.Entries[2].Enabled = false;
    var handle = h.StartHandle();
    var schedule = new QueueRunSchedule(QueueRunPlan.Build(h.Template.Entries.ToArray(), 0), handle.RunStartedAt, false);
    handle.Schedule = schedule;
    schedule.MarkOncePerRunCompleted(0);
    handle.SetCurrentSequence("B", Now);

    var snap = await h.Service.BuildAsync("q1");

    snap.EntryStates.Should().NotBeNull();
    snap.EntryStates!["e-a"].Should().Be("idle");
    snap.EntryStates["e-b"].Should().Be("running");
    snap.EntryStates.Should().NotContainKey("e-d");
  }

  [Fact]
  public async Task AQueuedRequestShowsFirstInUpcomingWithKindRunNext() {
    var h = new Harness();
    h.AddQueue();
    h.Entry("A", "Alpha");
    h.Entry("B", "Bravo");
    h.Template.Entries[0].EntryId = "e-a";
    h.Template.Entries[1].EntryId = "e-b";
    var handle = h.StartHandle();
    var schedule = new QueueRunSchedule(QueueRunPlan.Build(h.Template.Entries.ToArray(), 0), handle.RunStartedAt, false);
    handle.Schedule = schedule;
    schedule.MarkOncePerRunCompleted(0);
    schedule.MarkOncePerRunCompleted(1);
    schedule.MarkOncePerRunPassDone();
    handle.TryAddRunNext(new PendingRunNext("e-a", "A", ParameterScope.Empty, Now));

    var snap = await h.Service.BuildAsync("q1");

    var first = snap.Upcoming[0];
    first.ScheduleKind.Should().Be(ScheduleKind.RunNext);
    first.SequenceId.Should().Be("A");
    first.Reason.Should().Be("Run next");
    first.RelativeLabel.Should().Be("next");
    snap.NothingScheduled.Should().BeFalse();
    snap.EntryStates!["e-a"].Should().Be("queued");
  }
}
