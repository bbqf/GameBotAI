using System;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using GameBot.Domain.Commands.SelfReschedule;
using GameBot.Domain.Queues;
using GameBot.Service.Services.QueueExecution;
using Xunit;

#pragma warning disable CA2007, CA1861, CA1859

namespace GameBot.UnitTests.Queues;

/// <summary>Feature 189: the monitor shows a postponed booking and the smart scheduling settings of the stored queue.</summary>
public sealed partial class QueueMonitorServiceTests {
  [Fact]
  public async Task APostponedFiringShowsTheBookedTimeTheRequestedTimeAndThePostponedReason() {
    var h = new Harness();
    h.AddQueue();
    h.Sequences.Add("R", "Rescheduled");
    var handle = h.StartHandle();
    var requested = Now + TimeSpan.FromMinutes(2);
    var booked = Now + TimeSpan.FromMinutes(10);
    handle.AddTimerFiring(new SelfRescheduleEntry("f1", "R", SelfRescheduleOption.Timer, booked,
      Postponement: new SmartPostponement(requested, "A", SmartTargetKind.SelfReschedule)));

    var snap = await h.Service.BuildAsync("q1");

    var item = snap.Upcoming.Single(i => i.SequenceId == "R");
    item.ExpectedAt.Should().Be(booked);
    item.RequestedAt.Should().Be(requested);
    item.Reason.Should().Be("Rescheduled by a sequence, postponed from 12:02 (smart scheduling)");
  }

  [Fact]
  public async Task AFiringThatIsNotPostponedHasNoRequestedTimeAndTheReasonOfToday() {
    var h = new Harness();
    h.AddQueue();
    h.Sequences.Add("R", "Rescheduled");
    var handle = h.StartHandle();
    handle.AddTimerFiring(new SelfRescheduleEntry("f1", "R", SelfRescheduleOption.Timer, Now + TimeSpan.FromMinutes(7)));

    var snap = await h.Service.BuildAsync("q1");

    var item = snap.Upcoming.Single(i => i.SequenceId == "R");
    item.RequestedAt.Should().BeNull();
    item.Reason.Should().Be("Rescheduled by a sequence");
  }

  [Theory]
  [InlineData(true)]
  [InlineData(false)]
  public async Task TheSnapshotHasTheSettingsOfTheStoredQueue(bool running) {
    var h = new Harness();
    h.AddQueue();
    var stored = await h.Queues.GetAsync("q1");
    stored!.SmartScheduling = true;
    stored.SmartSchedulingWindowMinutes = 20;
    if (running) h.StartHandle();

    var snap = await h.Service.BuildAsync("q1");

    snap.Running.Should().Be(running);
    snap.SmartScheduling.Should().Be(new QueueMonitorSmartScheduling(true, 20));
  }

  [Fact]
  public async Task AQueueThatIsNotFoundGivesOffAnd15() {
    var h = new Harness();

    var snap = await h.Service.BuildAsync("nope");

    snap.SmartScheduling.Should().Be(new QueueMonitorSmartScheduling(false, 15));
  }
}
