using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace GameBot.Domain.Notifications {
  /// <summary>
  /// How much a queue tells the operator (feature 120). The JSON text is <c>none</c>, <c>failure</c>
  /// or <c>successAndFailure</c>. A queue file with no value reads as <see cref="None"/>.
  /// </summary>
  [JsonConverter(typeof(NotificationLevelJsonConverter))]
  public enum NotificationLevel {
    /// <summary>Send no message. This is the default.</summary>
    None = 0,

    /// <summary>Send the failure, cancelled and recovered messages.</summary>
    Failure = 1,

    /// <summary>Send all messages.</summary>
    SuccessAndFailure = 2
  }

  /// <summary>Reads and writes <see cref="NotificationLevel"/> as camel-case text.</summary>
  public sealed class NotificationLevelJsonConverter : JsonStringEnumConverter<NotificationLevel> {
    public NotificationLevelJsonConverter() : base(JsonNamingPolicy.CamelCase, allowIntegerValues: false) { }
  }

  /// <summary>Text conversion helpers for <see cref="NotificationLevel"/>.</summary>
  public static class NotificationLevelText {
    public static string ToText(NotificationLevel level) => level switch {
      NotificationLevel.None => "none",
      NotificationLevel.Failure => "failure",
      NotificationLevel.SuccessAndFailure => "successAndFailure",
      _ => "none"
    };

    /// <summary>Parses the JSON text of a level. Returns false for an absent or unknown value.</summary>
    public static bool TryParse(string? text, out NotificationLevel level) {
      level = NotificationLevel.None;
      if (string.IsNullOrWhiteSpace(text)) return false;
      var trimmed = text.Trim();
      if (string.Equals(trimmed, "none", StringComparison.OrdinalIgnoreCase)) return true;
      if (string.Equals(trimmed, "failure", StringComparison.OrdinalIgnoreCase)) { level = NotificationLevel.Failure; return true; }
      if (string.Equals(trimmed, "successAndFailure", StringComparison.OrdinalIgnoreCase)) { level = NotificationLevel.SuccessAndFailure; return true; }
      return false;
    }
  }
}
