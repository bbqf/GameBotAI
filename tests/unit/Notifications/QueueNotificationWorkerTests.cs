using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using GameBot.Domain.Notifications;
using GameBot.Service.Services.Notifications;
using Xunit;

#pragma warning disable CA2007, CA1861, CA1859, CA1707

namespace GameBot.UnitTests.Notifications;

/// <summary>Feature 120: the state table of research R-003, and the flows V-03 to V-08.</summary>
public sealed class QueueNotificationWorkerTests {
  private const string Red = "\U0001F534";
  private const string Green = "\U0001F7E2";
  private const string Yellow = "\U0001F7E1";

  private static string Text(string circle, string status) => $"Farm-1 : PNS.Collect : {circle} {status}";

  public static IEnumerable<object[]> StateTable() {
    const string s = "Success";
    const string f = "Failure";
    const string c = "Cancelled";

    // level, run sequence, expected messages
    yield return new object[] { NotificationLevel.None, new[] { f, s, c }, Array.Empty<string>() };
    yield return new object[] { NotificationLevel.Failure, new[] { f }, new[] { Text(Red, "failure") } };
    yield return new object[] { NotificationLevel.Failure, new[] { f, f }, new[] { Text(Red, "failure") } };
    yield return new object[] { NotificationLevel.Failure, new[] { f, s }, new[] { Text(Red, "failure"), Text(Green, "recovered") } };
    yield return new object[] { NotificationLevel.Failure, new[] { s }, Array.Empty<string>() };
    yield return new object[] { NotificationLevel.Failure, new[] { c }, new[] { Text(Yellow, "cancelled") } };
    yield return new object[] { NotificationLevel.SuccessAndFailure, new[] { f }, new[] { Text(Red, "failure") } };
    yield return new object[] { NotificationLevel.SuccessAndFailure, new[] { f, f }, new[] { Text(Red, "failure") } };
    yield return new object[] { NotificationLevel.SuccessAndFailure, new[] { f, s }, new[] { Text(Red, "failure"), Text(Green, "recovered") } };
    yield return new object[] { NotificationLevel.SuccessAndFailure, new[] { s }, new[] { Text(Green, "success") } };
    yield return new object[] { NotificationLevel.SuccessAndFailure, new[] { c }, new[] { Text(Yellow, "cancelled") } };
  }

  [Theory]
  [MemberData(nameof(StateTable))]
  public async Task V03_StateTable(NotificationLevel level, string[] runNames, string[] expected) {
    var runs = runNames.Select(n => System.Enum.Parse<NotificationRunStatus>(n)).ToArray();
    await using var h = new NotificationHarness(level);

    var messages = await h.RunAsync(runs);

    messages.Should().Equal(expected);
  }

  [Theory]
  [InlineData(NotificationLevel.Failure)]
  [InlineData(NotificationLevel.SuccessAndFailure)]
  public async Task V04_TenFailuresThenOneSuccessGiveTwoMessages(NotificationLevel level) {
    await using var h = new NotificationHarness(level);
    var runs = Enumerable.Repeat(NotificationRunStatus.Failure, 10).Append(NotificationRunStatus.Success).ToArray();

    var messages = await h.RunAsync(runs);

    messages.Should().Equal(Text(Red, "failure"), Text(Green, "recovered"));
  }

  [Fact]
  public async Task V05_SuccessFailureSuccessSuccessAtSuccessAndFailure() {
    await using var h = new NotificationHarness(NotificationLevel.SuccessAndFailure);
    var s = NotificationRunStatus.Success;

    var messages = await h.RunAsync(s, NotificationRunStatus.Failure, s, s);

    messages.Should().Equal(Text(Green, "success"), Text(Red, "failure"), Text(Green, "recovered"), Text(Green, "success"));
  }

  [Fact]
  public async Task V06_CancelledChangesNoStreak() {
    await using var h = new NotificationHarness(NotificationLevel.Failure);

    var messages = await h.RunAsync(
      NotificationRunStatus.Failure, NotificationRunStatus.Cancelled, NotificationRunStatus.Failure, NotificationRunStatus.Success);

    messages.Should().Equal(Text(Red, "failure"), Text(Yellow, "cancelled"), Text(Green, "recovered"));
  }

  [Fact]
  public async Task V08_LevelChangeBetweenFailureAndBothKeepsTheOpenStreak() {
    await using var h = new NotificationHarness(NotificationLevel.Failure);
    await h.HandleAsync(NotificationRunStatus.Failure);
    (await h.Queues.GetAsync("q1"))!.NotificationLevel = NotificationLevel.SuccessAndFailure;

    await h.HandleAsync(NotificationRunStatus.Success);
    await h.WaitIdleAsync();

    h.Channel.Sent.Select(m => m.Text).Should().Equal(Text(Red, "failure"), Text(Green, "recovered"));
  }

  [Fact]
  public async Task AJobForADeletedQueueIsDropped() {
    await using var h = new NotificationHarness(NotificationLevel.Failure);

    var messages = await h.RunAsync(NotificationRunStatus.Failure);
    h.Queues.Remove("q1");
    await h.HandleAsync(NotificationRunStatus.Failure);
    await h.WaitIdleAsync();

    messages.Should().HaveCount(1);
    h.Channel.Calls.Should().Be(1);
  }

  [Fact]
  public async Task AnUnreadableQueueDropsTheJobAndTheWorkerGoesOn() {
    await using var h = new NotificationHarness(NotificationLevel.Failure);
    var throwing = new ThrowingQueues();
    using var worker = new QueueNotificationWorker(h.Dispatcher, throwing, h.Sequences, h.Targets, new INotificationChannel[] { h.Channel }, h.WorkerLog);

    await worker.HandleAsync(NotificationWork.ForJob(NotificationHarness.Job(NotificationRunStatus.Failure)));

    h.Channel.Calls.Should().Be(0);
    h.WorkerLog.Lines.Should().ContainSingle(l => l.Contains("could not be read", StringComparison.Ordinal));
  }

  [Fact]
  public async Task AnUnreadableSequenceNameFallsBackToTheId() {
    await using var h = new NotificationHarness(NotificationLevel.Failure);
    h.Sequences.GetThrows = true;

    var messages = await h.RunAsync(NotificationRunStatus.Failure);

    messages.Should().Equal($"Farm-1 : s1 : {Red} failure");
  }

  [Fact]
  public async Task ADisabledTargetGetsNoMessageAndAnUnknownTypeIsSkippedWithALogLine() {
    await using var h = new NotificationHarness(NotificationLevel.Failure, targetCount: 1);
    var disabled = new NotificationTarget { Id = "off", Type = "telegram", Name = "Off", Enabled = false };
    h.Targets.Create(disabled);
    h.Targets.Create(new NotificationTarget { Id = "odd", Type = "smoke-signal", Name = "Odd" });

    var messages = await h.RunAsync(NotificationRunStatus.Failure);

    messages.Should().HaveCount(1);
    h.Channel.Sent.Should().OnlyContain(m => m.TargetId == "t0");
    h.WorkerLog.Lines.Should().ContainSingle(l => l.Contains("unknown type", StringComparison.Ordinal));
  }

  [Fact]
  public async Task AFailedSendIsLoggedWithTheReasonAndNoRetryOfTheStreak() {
    await using var h = new NotificationHarness(NotificationLevel.Failure);
    h.Channel.Behavior = (_, _, _) => Task.FromResult(NotificationSendResult.Failed("Telegram answered 400: chat not found"));

    await h.HandleAsync(NotificationRunStatus.Failure);
    await h.WaitIdleAsync();
    await h.HandleAsync(NotificationRunStatus.Failure);
    await h.WaitIdleAsync();

    h.Channel.Calls.Should().Be(1);
    h.WorkerLog.Lines.Should().ContainSingle(l => l.Contains("chat not found", StringComparison.Ordinal));
  }

  [Fact]
  public async Task AChannelThatThrowsIsLoggedAndNothingElseChanges() {
    await using var h = new NotificationHarness(NotificationLevel.Failure);
    h.Channel.Behavior = (_, _, _) => throw new InvalidOperationException("boom");

    await h.HandleAsync(NotificationRunStatus.Failure);
    await h.WaitIdleAsync();

    h.WorkerLog.Lines.Should().ContainSingle(l => l.Contains("faulted", StringComparison.Ordinal) && l.Contains("InvalidOperationException", StringComparison.Ordinal));
  }

  [Fact]
  public async Task TheWorkerLoopHandlesQueuedJobs() {
    await using var h = new NotificationHarness(NotificationLevel.Failure);
    await h.Worker.StartAsync(CancellationTokenNone());

    h.Dispatcher.Enqueue(NotificationHarness.Job(NotificationRunStatus.Failure));
    await NotificationHarness.WaitForAsync(() => h.Channel.Calls == 1);

    h.Channel.Sent.Should().ContainSingle();
    await NotificationHarness.WaitForAsync(() => h.Dispatcher.QueuedRunJobs == 0);
    h.Dispatcher.QueuedRunJobs.Should().Be(0);
  }

  [Fact]
  public async Task TheSendCapDropsTheMessageWithOneLogLine() {
    var limits = new NotificationDispatchLimits { MaxParallelSends = 1 };
    await using var h = new NotificationHarness(NotificationLevel.Failure, limits);
    var release = new TaskCompletionSource();
    h.Channel.Behavior = async (_, _, _) => { await release.Task; return NotificationSendResult.Ok(); };
    h.Sequences.Add("s2", "Other");

    await h.HandleAsync(NotificationRunStatus.Failure);
    await h.HandleAsync(NotificationRunStatus.Failure, sequenceId: "s2");
    release.SetResult();
    await h.WaitIdleAsync();

    h.Channel.Calls.Should().Be(1);
    h.WorkerLog.Lines.Should().ContainSingle(l => l.Contains("sends already run", StringComparison.Ordinal));
  }

  [Fact]
  public async Task V25_ASlowFirstSendEndsBeforeTheNextSendOfTheSamePairStarts() {
    await using var h = new NotificationHarness(NotificationLevel.SuccessAndFailure);
    var running = 0;
    var maxRunning = 0;
    h.Channel.Behavior = async (_, text, ct) => {
      var now = Interlocked.Increment(ref running);
      InterlockedMax(ref maxRunning, now);
      // The first message (failure) is slower than the second message (recovered).
      await Task.Delay(text.Contains("failure", StringComparison.Ordinal) ? 150 : 1, ct);
      Interlocked.Decrement(ref running);
      return NotificationSendResult.Ok();
    };

    await h.HandleAsync(NotificationRunStatus.Failure);
    await h.HandleAsync(NotificationRunStatus.Success);
    await NotificationHarness.WaitForAsync(() => h.Channel.Sent.Count == 2);

    maxRunning.Should().Be(1);
    h.Channel.Sent.Select(m => m.Text).Should().Equal(Text(Red, "failure"), Text(Green, "recovered"));
  }

  [Fact]
  public async Task V25_AStuckSendOfOnePairDoesNotHoldBackAnotherPair() {
    await using var h = new NotificationHarness(NotificationLevel.Failure);
    h.Sequences.Add("s2", "Other");
    var release = new TaskCompletionSource();
    h.Channel.Behavior = async (_, text, _) => {
      if (text.Contains("PNS.Collect", StringComparison.Ordinal)) await release.Task;
      return NotificationSendResult.Ok();
    };

    await h.HandleAsync(NotificationRunStatus.Failure);
    await h.HandleAsync(NotificationRunStatus.Success);
    await h.HandleAsync(NotificationRunStatus.Failure, sequenceId: "s2");
    await NotificationHarness.WaitForAsync(() => h.Channel.Sent.Count == 1);

    h.Channel.Sent.Select(m => m.Text).Should().Equal($"Farm-1 : Other : {Red} failure");
    release.SetResult();
    await NotificationHarness.WaitForAsync(() => h.Channel.Sent.Count == 3);
    h.Channel.Sent.Select(m => m.Text).Skip(1).Should().Equal(Text(Red, "failure"), Text(Green, "recovered"));
  }

  private static void InterlockedMax(ref int target, int value) {
    int current;
    while (value > (current = Volatile.Read(ref target))) {
      if (Interlocked.CompareExchange(ref target, value, current) == current) return;
    }
  }

  private static System.Threading.CancellationToken CancellationTokenNone() => System.Threading.CancellationToken.None;

  private sealed class ThrowingQueues : GameBot.Domain.Queues.IQueueRepository {
    public Task<GameBot.Domain.Queues.ExecutionQueue?> GetAsync(string id) => throw new InvalidOperationException("corrupt");

    public Task<IReadOnlyList<GameBot.Domain.Queues.ExecutionQueue>> ListAsync() => throw new NotSupportedException();

    public Task<GameBot.Domain.Queues.ExecutionQueue> CreateAsync(GameBot.Domain.Queues.ExecutionQueue queue) => throw new NotSupportedException();

    public Task<GameBot.Domain.Queues.ExecutionQueue> UpdateAsync(GameBot.Domain.Queues.ExecutionQueue queue) => throw new NotSupportedException();

    public Task<bool> DeleteAsync(string id) => throw new NotSupportedException();
  }
}
