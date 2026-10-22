using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using GameBot.Domain.Actions;
using GameBot.Domain.Commands;
using GameBot.Domain.Commands.SelfReschedule;
using GameBot.Domain.Notifications;
using GameBot.Domain.Parameters;
using GameBot.Domain.Services;
using GameBot.Service.Services.Notifications;
using GameBot.Service.Services.QueueExecution;
using GameBot.Service.Services.SequenceExecution;
using GameBot.Service.Services.StepThrough;
using GameBot.UnitTests.Notifications;
using Xunit;

#pragma warning disable CA2007, CA1861, CA1859, CA1707, CA2000

namespace GameBot.UnitTests.Sequences;

/// <summary>
/// The <c>notify</c> step sends its message to the enabled notification targets when it has no
/// <c>url</c> (issue #335).
/// </summary>
public sealed class NotifyStepTargetDeliveryTests {
  private sealed class FakeNotifier : IFailureNotifier {
    private int _calls;
    public int Calls => Volatile.Read(ref _calls);
    public string? LastUrl { get; private set; }
    public string? LastMessage { get; private set; }

    public Task<FailureNotificationResult> NotifyAsync(
      FailureNotificationEvent evt, string? overrideUrl, CancellationToken ct = default) {
      Interlocked.Increment(ref _calls);
      LastUrl = overrideUrl;
      LastMessage = evt.Message;
      return Task.FromResult(new FailureNotificationResult(true, DateTimeOffset.Now, null));
    }
  }

  // A dispatcher that counts the text sends. It can answer never, or answer with a given outcome.
  private sealed class FakeDispatcher : INotificationDispatcher {
    private int _sendTextCalls;
    public int SendTextCalls => Volatile.Read(ref _sendTextCalls);
    public bool NeverAnswers { get; init; }

    public void Enqueue(QueueNotificationJob job) { }

    public void ResetStreaks(string queueId) { }

    public void SendAlert(QueueAlert alert) { }

    public Task<TextSendOutcome> SendText(string text, string scopeKey, string sequenceId, CancellationToken ct) {
      Interlocked.Increment(ref _sendTextCalls);
      return NeverAnswers
        ? new TaskCompletionSource<TextSendOutcome>().Task
        : Task.FromResult(TextSendOutcome.Delivered());
    }
  }

  private sealed class StubCoordinator : ISelfRescheduleCoordinator {
    public SelfRescheduleResult ScheduleSelf(string queueId, string sequenceId, SelfRescheduleOption option, TimeOnly? timerTimeOfDay, TimeSpan? timerRelativeOffset, ParameterScope? scope = null, SelfRescheduleKeep keep = SelfRescheduleKeep.None, string? runId = null) =>
      throw new NotSupportedException();

    public SelfRescheduleCancelResult CancelSelf(string queueId, string sequenceId) => throw new NotSupportedException();

    public DateTimeOffset ResolveTimerFireAt(TimeOnly? timerTimeOfDay, TimeSpan? timerRelativeOffset) => DateTimeOffset.UtcNow;
  }

  private static SequenceActionPayload NotifyAction(string message, string? url = null) {
    var action = new SequenceActionPayload { Type = ActionTypes.Notify };
    action.Parameters["message"] = message;
    if (url is not null) action.Parameters["url"] = url;
    return action;
  }

  // The one place that builds the service.
  private static SequenceExecutionService NewService(
    NotificationHarness harness,
    IFailureNotifier? notifier,
    INotificationDispatcher? dispatcher = null,
    TimeSpan? guard = null)
    => new(
      runner: null!, evalSvc: null!, imageVisibleConditionAdapter: null!, imageRepository: null!,
      executionLogService: null!, commandRepository: null!, sequenceRepository: harness.Sequences, commandExecutor: null!,
      selfRescheduleCoordinator: null!, sessionManager: null!, ensureGameRunning: null!,
      ensureEmulatorRunning: null!, sessionService: null!, ocrOffsetResolver: null!,
      notifier: notifier,
      notificationDispatcher: dispatcher ?? harness.Dispatcher,
      textSendGuard: guard);

  private static Task<ActionDispatchResult> RunStepAsync(SequenceExecutionService service, SequenceActionPayload action)
    => service.DispatchActionAsync(action, "s1", null, null, ParameterScope.Empty, null, CancellationToken.None);

  [Fact]
  public async Task MessageWithoutUrl_IsSentToTheEnabledTarget() {
    await using var harness = new NotificationHarness(targetCount: 1);
    await harness.Worker.StartAsync(CancellationToken.None);
    var service = NewService(harness, new FakeNotifier());

    var result = await RunStepAsync(service, NotifyAction("Screen is stuck"));

    result.Outcome.Should().Be("success");
    await NotificationHarness.WaitForAsync(() => harness.Channel.Sent.Count >= 1);
    harness.Channel.Sent.Select(s => s.Text).Should().ContainSingle().Which.Should().Be("Screen is stuck");
  }

  [Fact]
  public async Task TwoTargetsAndADisabledTarget_OnlyEnabledTargetsGetTheMessage() {
    await using var harness = new NotificationHarness(targetCount: 2);
    harness.Targets.Create(new NotificationTarget { Id = "off", Type = "telegram", Name = "Off", Enabled = false });
    await harness.Worker.StartAsync(CancellationToken.None);
    var notifier = new FakeNotifier();
    var service = NewService(harness, notifier);

    var result = await RunStepAsync(service, NotifyAction("Hello"));

    result.Outcome.Should().Be("success");
    result.Message.Should().Be("notification delivered");
    result.Message.Should().NotContain("no destination configured");
    harness.Channel.Sent.Select(s => s.TargetId).Should().BeEquivalentTo(new[] { "t0", "t1" });
    notifier.Calls.Should().Be(0);
  }

  [Fact]
  public async Task NoEnabledTarget_UsesTheOldNotifierPathAndTheStepStaysSuccess() {
    await using var harness = new NotificationHarness(targetCount: 0);
    await harness.Worker.StartAsync(CancellationToken.None);
    var notifier = new FakeNotifier();
    var service = NewService(harness, notifier);

    var result = await RunStepAsync(service, NotifyAction("Hello"));

    result.Outcome.Should().Be("success");
    notifier.Calls.Should().Be(1);
    notifier.LastMessage.Should().Be("Hello");
    notifier.LastUrl.Should().BeNull();
    harness.Channel.Calls.Should().Be(0);
  }

  [Fact]
  public async Task DeliveryFault_KeepsTheStepSuccessAndSaysNotDelivered() {
    await using var harness = new NotificationHarness(targetCount: 1);
    harness.Channel.Behavior = (_, _, _) => Task.FromResult(NotificationSendResult.Failed("chat not found"));
    await harness.Worker.StartAsync(CancellationToken.None);
    var service = NewService(harness, new FakeNotifier());

    var result = await RunStepAsync(service, NotifyAction("Hello"));

    result.Outcome.Should().Be("success");
    result.Message.Should().Be("notification not delivered: chat not found");
  }

  [Fact]
  public async Task SendThatNeverEnds_EndsAtTheGuardTimeLimitAndTheStepStaysSuccess() {
    await using var harness = new NotificationHarness(targetCount: 1);
    var dispatcher = new FakeDispatcher { NeverAnswers = true };
    var service = NewService(harness, new FakeNotifier(), dispatcher, TimeSpan.FromMilliseconds(150));

    var result = await RunStepAsync(service, NotifyAction("Hello"));

    result.Outcome.Should().Be("success");
    result.Message.Should().Be("notification not delivered: time limit");
    dispatcher.SendTextCalls.Should().Be(1);
  }

  [Fact]
  public void DefaultGuardTimeLimit_IsTheSendTimeoutPlusFiveSeconds() {
    var expected = new NotificationDispatchLimits().SendTimeout + QueueNotificationDispatcher.TextSendGuardMargin;

    expected.Should().Be(TimeSpan.FromSeconds(35));
  }

  [Fact]
  public async Task StepThroughPreview_SendsNothing() {
    // The step-through path for notify is StepThroughActionPreview.Describe. Its outcome is "previewed"
    // (SequenceStepper.PreviewOutcome). It is a static method and has no access to the dispatcher.
    await using var harness = new NotificationHarness(targetCount: 1);
    await harness.Worker.StartAsync(CancellationToken.None);
    var dispatcher = new FakeDispatcher();
    var service = NewService(harness, new FakeNotifier(), dispatcher);

    var preview = StepThroughActionPreview.Describe(NotifyAction("Hello"), new StubCoordinator(), DateTimeOffset.UtcNow);

    preview.Outcome.Should().Be("previewed");
    dispatcher.SendTextCalls.Should().Be(0);
    harness.Channel.Calls.Should().Be(0);
    service.Should().NotBeNull();
  }

  [Fact]
  public async Task UrlOverride_SendsOnlyToTheUrlAndNotToTheTargets() {
    await using var harness = new NotificationHarness(targetCount: 1);
    await harness.Worker.StartAsync(CancellationToken.None);
    var notifier = new FakeNotifier();
    var dispatcher = new FakeDispatcher();
    var service = NewService(harness, notifier, dispatcher);

    var result = await RunStepAsync(service, NotifyAction("Hello", "https://alerts.example/hook"));

    result.Outcome.Should().Be("success");
    notifier.Calls.Should().Be(1);
    notifier.LastUrl.Should().Be("https://alerts.example/hook");
    dispatcher.SendTextCalls.Should().Be(0);
    harness.Channel.Calls.Should().Be(0);
  }
}
