using System;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using GameBot.Domain.Notifications;
using GameBot.Service.Services.Notifications;
using Xunit;

#pragma warning disable CA2007, CA1861, CA1859, CA1707

namespace GameBot.UnitTests.Notifications;

/// <summary>Feature 120 (V-07, FR-012, FR-023): the reset control message and the job cap.</summary>
public sealed class QueueNotificationResetTests {
  private const string Red = "\U0001F534";

  [Fact]
  public async Task V07_ResetAtLevelNoneClosesTheStreakAtOnceWithNoMessage() {
    await using var h = new NotificationHarness(NotificationLevel.Failure);
    await h.HandleAsync(NotificationRunStatus.Failure);
    await h.WaitIdleAsync();
    h.Worker.Streaks.OpenCount("q1").Should().Be(1);
    h.Channel.Calls.Should().Be(1);

    (await h.Queues.GetAsync("q1"))!.NotificationLevel = NotificationLevel.None;
    await h.Worker.HandleAsync(NotificationWork.ForReset("q1"));

    h.Worker.Streaks.OpenCount("q1").Should().Be(0);
    h.Channel.Calls.Should().Be(1);

    // The next failure at level Failure sends one new "failure" message.
    (await h.Queues.GetAsync("q1"))!.NotificationLevel = NotificationLevel.Failure;
    await h.HandleAsync(NotificationRunStatus.Failure);
    await h.WaitIdleAsync();
    h.Channel.Sent.Select(m => m.Text).Should().Equal($"Farm-1 : PNS.Collect : {Red} failure", $"Farm-1 : PNS.Collect : {Red} failure");
  }

  [Fact]
  public async Task AResetIsNeverDroppedWhen256JobsWait() {
    await using var h = new NotificationHarness(NotificationLevel.Failure);
    await h.HandleAsync(NotificationRunStatus.Failure);
    h.Worker.Streaks.OpenCount("q1").Should().Be(1);

    // The worker loop does not run, so all jobs wait.
    for (var i = 0; i < 300; i++) h.Dispatcher.Enqueue(NotificationHarness.Job(NotificationRunStatus.Failure, sequenceId: $"x{i}"));
    h.Dispatcher.QueuedRunJobs.Should().Be(256);
    h.Dispatcher.ResetStreaks("q1");

    await h.Worker.StartAsync(System.Threading.CancellationToken.None);
    // The worker is the only user of the streak state, so read it only after the worker loop ended.
    await h.DrainAsync();
    // The reset came last in the channel, so it removed every key.
    h.Worker.Streaks.OpenCount("q1").Should().Be(0);
  }

  [Fact]
  public async Task ADeleteResetRemovesAllKeysOfTheQueueOnly() {
    await using var h = new NotificationHarness(NotificationLevel.Failure);
    h.Queues.Add(new GameBot.Domain.Queues.ExecutionQueue { Id = "q2", Name = "Other", EmulatorSerial = "e2", NotificationLevel = NotificationLevel.Failure });
    h.Sequences.Add("s2", "Two");
    await h.HandleAsync(NotificationRunStatus.Failure, "q1", "s1");
    await h.HandleAsync(NotificationRunStatus.Failure, "q1", "s2");
    await h.HandleAsync(NotificationRunStatus.Failure, "q2", "s1");
    await h.WaitIdleAsync();

    await h.Worker.HandleAsync(NotificationWork.ForReset("q1"));

    h.Worker.Streaks.OpenCount("q1").Should().Be(0);
    h.Worker.Streaks.OpenCount("q2").Should().Be(1);
  }

  [Fact]
  public void TheDispatcherIgnoresABlankQueueIdAndNeverThrows() {
    var log = new CapturingLogger<QueueNotificationDispatcher>();
    var dispatcher = new QueueNotificationDispatcher(log);

    Action act = () => {
      dispatcher.ResetStreaks(" ");
      dispatcher.ResetStreaks("q1");
      dispatcher.Enqueue(NotificationHarness.Job(NotificationRunStatus.Success));
    };

    act.Should().NotThrow();
    dispatcher.QueuedRunJobs.Should().Be(1);
  }

  [Fact]
  public void TheDispatcherDropsAboveTheCapWithOneLogLineForEachDrop() {
    var log = new CapturingLogger<QueueNotificationDispatcher>();
    var dispatcher = new QueueNotificationDispatcher(log);

    for (var i = 0; i < 300; i++) dispatcher.Enqueue(NotificationHarness.Job(NotificationRunStatus.Failure, sequenceId: $"s{i}"));

    dispatcher.QueuedRunJobs.Should().Be(256);
    log.Lines.Should().HaveCount(44);
  }

  [Fact]
  public void TheStreakStateRefusesTwoUsersAtTheSameTime() {
    var state = new NotificationStreakState();
    state.Open("q", "s");
    state.IsOpen("q", "s").Should().BeTrue();
    state.TotalOpenCount.Should().Be(1);
    state.Close("q", "s");
    state.IsOpen("q", "s").Should().BeFalse();
  }

  [Fact]
  public void TheStreakStateThrowsWhenAnotherUserIsInsideAndLeavesTheCounterCorrect() {
    var state = new NotificationStreakState();
    var field = typeof(NotificationStreakState).GetField("_inside", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;

    // Set the state as if another thread is inside a method now.
    field.SetValue(state, 1);
    Action[] users = {
      () => state.IsOpen("q", "s"),
      () => state.Open("q", "s"),
      () => state.Close("q", "s"),
      () => state.RemoveQueue("q"),
      () => state.OpenCount("q"),
      () => _ = state.TotalOpenCount,
    };

    foreach (var user in users) {
      user.Should().Throw<InvalidOperationException>();
      field.GetValue(state).Should().Be(1);
    }

    // When the other user leaves, the state works again.
    field.SetValue(state, 0);
    state.Open("q", "s");
    state.TotalOpenCount.Should().Be(1);
    state.OpenCount("q").Should().Be(1);
    state.RemoveQueue("q").Should().Be(1);
  }

  [Fact]
  public void ANullJobIsLoggedAndNeverThrows() {
    var log = new CapturingLogger<QueueNotificationDispatcher>();
    var dispatcher = new QueueNotificationDispatcher(log);

    Action act = () => dispatcher.Enqueue(null!);

    act.Should().NotThrow();
    dispatcher.QueuedRunJobs.Should().Be(0);
    log.Lines.Should().ContainSingle(l => l.Contains("could not be queued", StringComparison.Ordinal));
  }

  [Fact]
  public void AJobAfterTheChannelIsClosedIsNotCountedAndNeverThrows() {
    var dispatcher = new QueueNotificationDispatcher(new CapturingLogger<QueueNotificationDispatcher>());
    dispatcher.Complete();

    Action act = () => {
      dispatcher.Enqueue(NotificationHarness.Job(NotificationRunStatus.Failure));
      dispatcher.ResetStreaks("q1");
    };

    act.Should().NotThrow();
    dispatcher.QueuedRunJobs.Should().Be(0);
  }
}
