namespace GameBot.Service.Services.Notifications;

/// <summary>
/// Makes the plain text of a queue message (feature 120): <c>&lt;queue&gt; : &lt;sequence&gt; :
/// &lt;circle&gt; &lt;status&gt;</c>. The text has no markup, so a name with a special character
/// cannot break the message. The channel sends it with no parse mode.
/// </summary>
internal static class NotificationMessageFormatter {
  /// <summary>
  /// Shows a client key in an error text only when it looks like a plain key name. A client can
  /// send any text as a key, so a key with another shape could repeat a secret.
  /// </summary>
  public static string SafeKey(string key) =>
    key.Length is > 0 and <= 40 && key.All(c => char.IsAsciiLetterOrDigit(c) || c == '_') ? key : "(invalid name)";

  /// <summary>The fixed text of the test message.</summary>
  public const string TestMessageText = "GameBot test message";

  private const string Green = "\U0001F7E2";
  private const string Red = "\U0001F534";
  private const string Yellow = "\U0001F7E1";

  /// <summary>
  /// Makes the message text. An absent or blank name is replaced by the ID.
  /// </summary>
  public static string Format(
    string? queueName,
    string queueId,
    string? sequenceName,
    string sequenceId,
    NotificationMessageStatus status) {
    var queue = string.IsNullOrWhiteSpace(queueName) ? queueId : queueName;
    var sequence = string.IsNullOrWhiteSpace(sequenceName) ? sequenceId : sequenceName;
    return $"{queue} : {sequence} : {Circle(status)} {StatusWord(status)}";
  }

  /// <summary>
  /// Makes the text of a device alert (feature 121). An absent or blank name is replaced by the ID.
  /// </summary>
  /// <param name="queueName">The queue name, or null.</param>
  /// <param name="queueId">The queue ID.</param>
  /// <param name="kind">The alert kind.</param>
  /// <param name="reason">The liveness reason. Used by <see cref="QueueAlertKind.NotLive"/>.</param>
  /// <param name="attempts">The count of attempts. Used by <see cref="QueueAlertKind.RecoveryFailed"/>.</param>
  public static string FormatAlert(string? queueName, string queueId, QueueAlertKind kind, string? reason, int attempts) {
    var queue = string.IsNullOrWhiteSpace(queueName) ? queueId : queueName;
    return kind switch {
      QueueAlertKind.NotLive => $"{Red} {queue} : device not live ({(string.IsNullOrWhiteSpace(reason) ? "unknown" : reason)})",
      QueueAlertKind.LiveAgain => $"{Green} {queue} : device live again",
      _ => $"{Red} {queue} : device recovery failed after {attempts.ToString(System.Globalization.CultureInfo.InvariantCulture)} attempts"
    };
  }

  public static string Circle(NotificationMessageStatus status) => status switch {
    NotificationMessageStatus.Success => Green,
    NotificationMessageStatus.Recovered => Green,
    NotificationMessageStatus.Failure => Red,
    _ => Yellow
  };

  public static string StatusWord(NotificationMessageStatus status) => status switch {
    NotificationMessageStatus.Success => "success",
    NotificationMessageStatus.Recovered => "recovered",
    NotificationMessageStatus.Failure => "failure",
    _ => "cancelled"
  };
}
