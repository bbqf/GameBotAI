using System;
using GameBot.Domain.Queues;
using GameBot.Service.Contracts.Queues;
using GameBot.Service.Services.Notifications;

namespace GameBot.Service.Endpoints;

/// <summary>
/// Validation and mapping for a queue's failure policy (feature 087, issue #181).
/// <para>
/// Lives outside <see cref="QueuesEndpoints"/> so the endpoint lambdas stay one-liners — the same
/// reason <c>ProjectMonitor</c> is factored out. Every failure returns a message naming the
/// offending value, per Constitution III.
/// </para>
/// </summary>
internal static class QueueFailurePolicyMapping {
  /// <summary>Accepted <c>action</c> values, echoed verbatim in the validation message.</summary>
  public const string AllowedActions = "notify, stop, pause, notifyAndStop";

  /// <summary>
  /// Validates a request DTO and converts it to the domain policy.
  /// <para>
  /// Returns <c>true</c> with <paramref name="policy"/> set (possibly null, when the request
  /// carried no policy at all) and <paramref name="error"/> null. Returns <c>false</c> with
  /// <paramref name="error"/> describing the first problem found.
  /// </para>
  /// </summary>
  /// <param name="dto">The request's policy, or null when none was supplied.</param>
  /// <param name="options">Service notification options, for the default-destination fallback.</param>
  /// <param name="policy">The mapped domain policy, or null when <paramref name="dto"/> was null.</param>
  /// <param name="error">Actionable validation message, or null on success.</param>
  public static bool TryMap(
    QueueFailurePolicyDto? dto,
    FailureNotificationOptions options,
    out QueueFailurePolicy? policy,
    out string? error) {
    policy = null;
    error = null;
    if (dto is null) return true;

    if (dto.ConsecutiveFailedCycles < 1) {
      error = $"failurePolicy.consecutiveFailedCycles must be at least 1 (was: {dto.ConsecutiveFailedCycles})";
      return false;
    }

    if (!TryParseAction(dto.Action, out var action)) {
      error = $"failurePolicy.action must be one of: {AllowedActions} (was: '{dto.Action}')";
      return false;
    }

    var url = string.IsNullOrWhiteSpace(dto.NotifyUrl) ? null : dto.NotifyUrl!.Trim();
    if (url is not null && !IsAbsoluteHttpUrl(url)) {
      error = $"failurePolicy.notifyUrl must be an absolute http or https URL (was: '{url}')";
      return false;
    }

    var mapped = new QueueFailurePolicy {
      ConsecutiveFailedCycles = dto.ConsecutiveFailedCycles,
      Action = action,
      NotifyUrl = url
    };

    // A notifying policy with no destination anywhere would look configured and never fire. That is
    // a configuration error, not a silent no-op (FR-006).
    if (mapped.Notifies && url is null && string.IsNullOrWhiteSpace(options.DefaultUrl)) {
      error = $"failurePolicy.action '{ActionToWire(action)}' requires a destination: "
            + "set failurePolicy.notifyUrl or Service:Notifications:DefaultUrl";
      return false;
    }

    policy = mapped;
    return true;
  }

  /// <summary>Projects a stored policy for a response; null in, null out. Never emits a credential.</summary>
  public static QueueFailurePolicyDto? Project(QueueFailurePolicy? policy) =>
    policy is null
      ? null
      : new QueueFailurePolicyDto {
        ConsecutiveFailedCycles = policy.ConsecutiveFailedCycles,
        Action = ActionToWire(policy.Action),
        NotifyUrl = policy.NotifyUrl
      };

  /// <summary>
  /// Value copy of a policy, so a duplicated queue does not share a mutable instance with its
  /// source. Null in, null out.
  /// </summary>
  public static QueueFailurePolicy? Clone(QueueFailurePolicy? policy) =>
    policy is null
      ? null
      : new QueueFailurePolicy {
        ConsecutiveFailedCycles = policy.ConsecutiveFailedCycles,
        Action = policy.Action,
        NotifyUrl = policy.NotifyUrl
      };

  /// <summary>The wire spelling of an action (camelCase), as published in the API contract.</summary>
  public static string ActionToWire(QueueFailureAction action) => action switch {
    QueueFailureAction.Notify => "notify",
    QueueFailureAction.Stop => "stop",
    QueueFailureAction.Pause => "pause",
    QueueFailureAction.NotifyAndStop => "notifyAndStop",
    _ => action.ToString()
  };

  /// <summary>Parses a wire action value, case-insensitively. Blank and unknown values both fail.</summary>
  public static bool TryParseAction(string? value, out QueueFailureAction action) {
    action = QueueFailureAction.Notify;
    if (string.IsNullOrWhiteSpace(value)) return false;
    switch (value.Trim().ToLowerInvariant()) {
      case "notify": action = QueueFailureAction.Notify; return true;
      case "stop": action = QueueFailureAction.Stop; return true;
      case "pause": action = QueueFailureAction.Pause; return true;
      case "notifyandstop": action = QueueFailureAction.NotifyAndStop; return true;
      default: return false;
    }
  }

  /// <summary>
  /// True for an absolute http/https URL. Anything else — a relative path, a bare host, another
  /// scheme — is rejected, because a destination must be unambiguous and reachable (FR-016).
  /// </summary>
  public static bool IsAbsoluteHttpUrl(string? value) =>
    Uri.TryCreate(value, UriKind.Absolute, out var uri)
    && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
}
