using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using GameBot.Domain.Actions;
using GameBot.Domain.Queues;
using GameBot.Domain.Commands;
using GameBot.Domain.Commands.Notify;
using GameBot.Domain.Commands.SelfReschedule;
using GameBot.Domain.Services;
using GameBot.Domain.Services.StepThrough;
using GameBot.Service.Services.QueueExecution;

namespace GameBot.Service.Services.StepThrough;

/// <summary>
/// Describes the effect of an action with an outside effect (<c>reschedule-self</c>, <c>notify</c>, and a restart step)
/// without applying it (feature 127, FR-015). It writes no queue state, sends no message, and reads no
/// screen.
/// </summary>
internal static class StepThroughActionPreview {
  private const int MaxMessageLength = 80;

  /// <summary>Returns a preview result. The message is the intended effect.</summary>
  public static ActionDispatchResult Describe(SequenceActionPayload action, ISelfRescheduleCoordinator coordinator, DateTimeOffset localNow) {
    ArgumentNullException.ThrowIfNull(action);
    ArgumentNullException.ThrowIfNull(coordinator);

    if (string.Equals(action.Type, ActionTypes.RescheduleSelf, StringComparison.OrdinalIgnoreCase)) {
      return new ActionDispatchResult(SequenceStepper.PreviewOutcome, DescribeReschedule(action, coordinator, localNow));
    }

    if (string.Equals(action.Type, ActionTypes.Notify, StringComparison.OrdinalIgnoreCase)) {
      return new ActionDispatchResult(SequenceStepper.PreviewOutcome, DescribeNotify(action));
    }

    if (string.Equals(action.Type, ActionTypes.EnsureGameRunning, StringComparison.OrdinalIgnoreCase)) {
      return new ActionDispatchResult(SequenceStepper.PreviewOutcome, "would stop the game, start it again, and wait for the foreground");
    }

    // Feature 142 (R9): a quiz step reads no screen and changes no store in a step-through.
    if (string.Equals(action.Type, ActionTypes.QuizLookup, StringComparison.OrdinalIgnoreCase)) {
      return new ActionDispatchResult(
        SequenceStepper.PreviewOutcome,
        "would read the question and the answers from the screen and look the question up in the quiz store (the screen is not read and the store is not used in a step-through)");
    }

    if (string.Equals(action.Type, ActionTypes.QuizLearn, StringComparison.OrdinalIgnoreCase)) {
      return new ActionDispatchResult(
        SequenceStepper.PreviewOutcome,
        "would store the correct answer of the lookup step in the quiz store (the store is not changed in a step-through)");
    }

    return new ActionDispatchResult(SequenceStepper.PreviewOutcome, $"would run '{action.Type}'");
  }

  private static string DescribeReschedule(SequenceActionPayload action, ISelfRescheduleCoordinator coordinator, DateTimeOffset localNow) {
    if (!SelfReschedulePayload.TryRead(action, out var payload, out var error) || payload is null) {
      return $"would not reschedule: {error}";
    }

    switch (payload.Option) {
      case SelfRescheduleOption.Cancel:
        return "would remove the pending bookings of this sequence";
      case SelfRescheduleOption.OncePerRun:
        return "would run this sequence again in this run";
      case SelfRescheduleOption.EveryStep:
        return "would run this sequence again after every step";
      case SelfRescheduleOption.AtQueueStart:
        return "would run this sequence again at the next start of the queue";
      case SelfRescheduleOption.Timer:
        if (payload.HasOcrOffset) {
          return "would reschedule after the time that the screen shows (the screen is not read in a step-through)";
        }

        var fireAt = coordinator.ResolveTimerFireAt(payload.TimerTimeOfDay, payload.TimerRelativeOffset);
        var sameDay = fireAt.Date == localNow.Date;
        var text = fireAt.ToString(sameDay ? "HH:mm" : "yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
        return $"would reschedule at {text}";
      default:
        return "would reschedule this sequence";
    }
  }

  /// <summary>
  /// Like <see cref="Describe"/>, and a notify step with a dedupe key also reads the sent-alert store
  /// (feature 137, FR-014). The read has no side effect. A step-through is not in a queue, so the scope
  /// is the ad hoc scope, as in a real run outside a queue.
  /// </summary>
  public static async Task<ActionDispatchResult> DescribeAsync(
      SequenceActionPayload action,
      ISelfRescheduleCoordinator coordinator,
      DateTimeOffset localNow,
      ISentAlertStore? store,
      TimeProvider timeProvider,
      CancellationToken ct) {
    ArgumentNullException.ThrowIfNull(action);
    if (!string.Equals(action.Type, ActionTypes.Notify, StringComparison.OrdinalIgnoreCase)) {
      return Describe(action, coordinator, localNow);
    }

    return new ActionDispatchResult(
      SequenceStepper.PreviewOutcome,
      await DescribeNotifyAsync(action, store, timeProvider, ct).ConfigureAwait(false));
  }

  private static async Task<string> DescribeNotifyAsync(SequenceActionPayload action, ISentAlertStore? store, TimeProvider timeProvider, CancellationToken ct) {
    if (store is null || !NotifyPayload.TryRead(action, out var payload, out _) || payload is null || !payload.HasDedupe) {
      return DescribeNotify(action);
    }

    var now = timeProvider.GetLocalNow();
    var windowStart = payload.WindowStart(now, timeProvider.LocalTimeZone);
    var sentAt = await store.WasSentSinceAsync(NotifyPayload.AdhocScope, payload.DedupeKey!, windowStart, ct).ConfigureAwait(false);
    if (sentAt is { } at) {
      return $"would suppress the notification: sent at {at.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)} in this window";
    }

    if (await store.HasLiveReservationAsync(NotifyPayload.AdhocScope, payload.DedupeKey!, now, ct).ConfigureAwait(false)) {
      return "would suppress the notification: another run is sending it now";
    }

    return DescribeNotify(action);
  }

  private static string DescribeNotify(SequenceActionPayload action) {
    if (!NotifyPayload.TryRead(action, out var payload, out var error) || payload is null) {
      return $"would not send a notification: {error}";
    }

    var message = payload.Message.Length <= MaxMessageLength ? payload.Message : payload.Message[..MaxMessageLength] + "...";
    return $"would send a notification: '{message}'";
  }
}
