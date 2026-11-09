using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using GameBot.Domain.Notifications;
using GameBot.Service.Services.Notifications;
using GameBot.Domain.Commands.Notify;
using Xunit;

#pragma warning disable CA2007, CA1861, CA1859, CA1707, CA2000

namespace GameBot.UnitTests.Notifications;

/// <summary>The text send of the notification worker (issue #335).</summary>
public sealed class QueueNotificationWorkerTextSendTests {
  private static async Task<TextSendOutcome> SendAsync(NotificationHarness h, string text = "Hello") {
    await h.Worker.StartAsync(CancellationToken.None);
    return await h.Dispatcher.SendText(text, "q1", "s1", CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(10));
  }

  [Fact]
  public async Task TwoEnabledTargets_EachGetsTheMessageOneTime() {
    await using var h = new NotificationHarness(targetCount: 2);

    var outcome = await SendAsync(h);

    outcome.State.Should().Be(TextSendState.Delivered);
    h.Channel.Sent.Should().HaveCount(2);
    h.Channel.Sent.Select(s => s.TargetId).Should().BeEquivalentTo(new[] { "t0", "t1" });
  }

  [Fact]
  public async Task DisabledTarget_GetsNoMessage() {
    await using var h = new NotificationHarness(targetCount: 1);
    h.Targets.Create(new NotificationTarget { Id = "off", Type = "telegram", Name = "Off", Enabled = false });

    var outcome = await SendAsync(h);

    outcome.State.Should().Be(TextSendState.Delivered);
    h.Channel.Sent.Select(s => s.TargetId).Should().BeEquivalentTo(new[] { "t0" });
  }

  [Fact]
  public async Task OneFailingTarget_DoesNotStopTheOther() {
    await using var h = new NotificationHarness(targetCount: 2);
    h.Channel.Behavior = (target, _, _) => target.Id == "t0"
      ? throw new InvalidOperationException("boom")
      : Task.FromResult(NotificationSendResult.Ok());

    var outcome = await SendAsync(h);

    outcome.State.Should().Be(TextSendState.Delivered);
    h.Channel.Sent.Select(s => s.TargetId).Should().BeEquivalentTo(new[] { "t1" });
  }

  [Fact]
  public async Task NoEnabledTarget_GivesNoTarget() {
    await using var h = new NotificationHarness(targetCount: 0);

    var outcome = await SendAsync(h);

    outcome.State.Should().Be(TextSendState.NoTarget);
    h.Channel.Calls.Should().Be(0);
  }

  [Fact]
  public async Task OnlyDisabledTargets_GiveNoTarget() {
    await using var h = new NotificationHarness(targetCount: 0);
    h.Targets.Create(new NotificationTarget { Id = "off", Type = "telegram", Name = "Off", Enabled = false });

    var outcome = await SendAsync(h);

    outcome.State.Should().Be(TextSendState.NoTarget);
  }

  [Fact]
  public async Task TargetListReadFault_GivesNoTarget() {
    var throwing = new ThrowingTargetStore();
    var dispatcher = new QueueNotificationDispatcher(new CapturingLogger<QueueNotificationDispatcher>());
    var worker = new QueueNotificationWorker(
      dispatcher, new MemoryQueueRepository(), new MemorySequenceRepository(), throwing,
      new INotificationChannel[] { new RecordingChannel() }, new CapturingLogger<QueueNotificationWorker>());
    await worker.StartAsync(CancellationToken.None);
    try {
      var outcome = await dispatcher.SendText("Hello", "q1", "s1", CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(10));

      outcome.State.Should().Be(TextSendState.NoTarget);
    }
    finally {
      dispatcher.Complete();
      await worker.StopAsync(CancellationToken.None);
      worker.Dispose();
    }
  }

  [Fact]
  public async Task AllTargetsFail_GiveFailedWithAReason() {
    await using var h = new NotificationHarness(targetCount: 2);
    h.Channel.Behavior = (_, _, _) => Task.FromResult(NotificationSendResult.Failed("chat not found"));

    var outcome = await SendAsync(h);

    outcome.State.Should().Be(TextSendState.Failed);
    outcome.Reason.Should().Be("chat not found");
  }

  [Fact]
  public async Task Text_HasTheQueueNameAsLabel() {
    await using var h = new NotificationHarness(targetCount: 1);

    await SendAsync(h, "Plain text 1");

    h.Channel.Sent.Single().Text.Should().Be("Farm-1 : Plain text 1");
  }

  [Fact]
  public async Task TwoQueues_GiveTwoDifferentLabels() {
    await using var h = new NotificationHarness(targetCount: 1);
    h.Queues.Add(new GameBot.Domain.Queues.ExecutionQueue { Id = "q2", Name = "Farm-2", EmulatorSerial = "emu-2" });
    await h.Worker.StartAsync(CancellationToken.None);

    await h.Dispatcher.SendText("Hello", "q1", "s1", CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(10));
    await h.Dispatcher.SendText("Hello", "q2", "s1", CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(10));

    h.Channel.Sent.Select(s => s.Text).Should().BeEquivalentTo(new[] { "Farm-1 : Hello", "Farm-2 : Hello" });
  }

  [Fact]
  public async Task BlankQueueName_UsesTheQueueIdAsLabel() {
    await using var h = new NotificationHarness(targetCount: 1);
    h.Queues.Add(new GameBot.Domain.Queues.ExecutionQueue { Id = "q-7", Name = "  ", EmulatorSerial = "emu-7" });
    await h.Worker.StartAsync(CancellationToken.None);

    await h.Dispatcher.SendText("Hello", "q-7", "s1", CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(10));

    h.Channel.Sent.Single().Text.Should().Be("q-7 : Hello");
  }

  [Fact]
  public async Task RenamedQueue_GivesTheNewNameInTheNextMessage() {
    await using var h = new NotificationHarness(targetCount: 1);
    await h.Worker.StartAsync(CancellationToken.None);
    await h.Dispatcher.SendText("Hello", "q1", "s1", CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(10));

    h.Queues.Add(new GameBot.Domain.Queues.ExecutionQueue { Id = "q1", Name = "Farm-C", EmulatorSerial = "emu-1" });
    await h.Dispatcher.SendText("Hello", "q1", "s1", CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(10));

    h.Channel.Sent.Select(s => s.Text).Should().Equal("Farm-1 : Hello", "Farm-C : Hello");
  }

  [Fact]
  public async Task AdhocScope_SendsThePlainTextAndReadsNoQueue() {
    await using var h = new NotificationHarness(targetCount: 1);
    h.Queues.GetThrows = true;
    await h.Worker.StartAsync(CancellationToken.None);

    var outcome = await h.Dispatcher.SendText("Hello", NotifyPayload.AdhocScope, "s1", CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(10));

    outcome.State.Should().Be(TextSendState.Delivered);
    h.Channel.Sent.Single().Text.Should().Be("Hello");
    h.WorkerLog.Lines.Should().NotContain(l => l.Contains("could not be read", StringComparison.Ordinal));
  }

  [Fact]
  public async Task MissingQueue_UsesTheQueueIdAsLabel() {
    await using var h = new NotificationHarness(targetCount: 1);
    await h.Worker.StartAsync(CancellationToken.None);

    var outcome = await h.Dispatcher.SendText("Hello", "gone", "s1", CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(10));

    outcome.State.Should().Be(TextSendState.Delivered);
    h.Channel.Sent.Single().Text.Should().Be("gone : Hello");
  }

  [Fact]
  public async Task QueueReadThatThrows_UsesTheQueueIdAndTheMessageIsDelivered() {
    await using var h = new NotificationHarness(targetCount: 1);
    h.Queues.GetThrows = true;
    await h.Worker.StartAsync(CancellationToken.None);

    var outcome = await h.Dispatcher.SendText("Hello", "q1", "s1", CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(10));

    outcome.State.Should().Be(TextSendState.Delivered);
    h.Channel.Sent.Single().Text.Should().Be("q1 : Hello");
    h.WorkerLog.Lines.Should().Contain(l => l.Contains("could not be read", StringComparison.Ordinal));
  }

  [Fact]
  public async Task PartialFailure_IsDeliveredAndTheLogNamesTheFailedCount() {
    await using var h = new NotificationHarness(targetCount: 2);
    h.Channel.Behavior = (target, _, _) => Task.FromResult(
      target.Id == "t0" ? NotificationSendResult.Failed("down") : NotificationSendResult.Ok());

    var outcome = await SendAsync(h);

    outcome.State.Should().Be(TextSendState.Delivered);
    h.WorkerLog.Lines.Should().Contain(l => l.Contains("1 of 2 targets did not accept it", StringComparison.Ordinal));
  }

  [Fact]
  public async Task TextSend_DoesNotChangeTheQueueNotificationOutput() {
    await using var h = new NotificationHarness(targetCount: 1);
    await SendAsync(h, "Free text");

    await h.HandleAsync(NotificationRunStatus.Failure);
    await h.WaitIdleAsync();

    var texts = h.Channel.Sent.Select(s => s.Text).ToList();
    texts.Should().HaveCount(2);
    texts[0].Should().Be("Farm-1 : Free text");
    texts[1].Should().Contain("Farm-1").And.NotBe("Free text");
  }

  private sealed class ThrowingTargetStore : INotificationTargetStore {
    public System.Collections.Generic.IReadOnlyList<NotificationTarget> List() => throw new InvalidOperationException("read failed");

    public NotificationTarget? Find(string id) => null;

    public NotificationTarget Create(NotificationTarget target) => target;

    public NotificationTarget? Update(NotificationTarget target) => null;

    public bool Delete(string id) => false;
  }
}
