using System;
using System.Globalization;
using GameBot.Domain.Actions;
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

  private static string DescribeNotify(SequenceActionPayload action) {
    if (!NotifyPayload.TryRead(action, out var payload, out var error) || payload is null) {
      return $"would not send a notification: {error}";
    }

    var message = payload.Message.Length <= MaxMessageLength ? payload.Message : payload.Message[..MaxMessageLength] + "...";
    return $"would send a notification: '{message}'";
  }
}
