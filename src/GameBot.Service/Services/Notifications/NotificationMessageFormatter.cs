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
