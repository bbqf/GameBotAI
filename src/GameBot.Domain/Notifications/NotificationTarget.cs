using System;
using System.Collections.Generic;

namespace GameBot.Domain.Notifications {
  /// <summary>
  /// One place that receives queue messages (feature 120). The store keeps targets in
  /// <c>data/notifications/targets.json</c>. The <see cref="Settings"/> map holds the values of the
  /// target type, so a new type needs no change to the store. For type <c>telegram</c> the keys are
  /// <c>botToken</c> (secret) and <c>chatId</c>.
  /// </summary>
  public sealed class NotificationTarget {
    /// <summary>Stable ID (GUID "N" format). The store makes it on create.</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>Target type, lower case. For example <c>telegram</c>.</summary>
    public string Type { get; set; } = string.Empty;

    /// <summary>Display name, 1 to 100 characters.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>A disabled target gets no queue message. The test action still works.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Values of the target type. Secret keys are in this map too.</summary>
    public Dictionary<string, string> Settings { get; init; } = new(StringComparer.Ordinal);

    public DateTimeOffset? CreatedAt { get; set; }

    public DateTimeOffset? UpdatedAt { get; set; }

    /// <summary>Makes a deep copy, so a caller cannot change the stored value.</summary>
    public NotificationTarget Clone() {
      var copy = new NotificationTarget {
        Id = Id,
        Type = Type,
        Name = Name,
        Enabled = Enabled,
        CreatedAt = CreatedAt,
        UpdatedAt = UpdatedAt
      };
      foreach (var (key, value) in Settings) copy.Settings[key] = value;
      return copy;
    }
  }
}
