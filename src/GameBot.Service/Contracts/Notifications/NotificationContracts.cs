using System;
using System.Collections.Generic;

namespace GameBot.Service.Contracts.Notifications {
  /// <summary>
  /// Request body to create or replace a notification target (feature 120). Secret values are in
  /// <see cref="Secrets"/>, never in <see cref="Settings"/>. On an update, an absent or empty secret
  /// keeps the stored secret.
  /// </summary>
  internal sealed class NotificationTargetRequest {
    /// <summary>Target type. For example <c>telegram</c>. Required.</summary>
    public string? Type { get; set; }

    /// <summary>Display name, 1 to 100 characters. Required.</summary>
    public string? Name { get; set; }

    /// <summary>Whether queue messages go to this target. Default true.</summary>
    public bool? Enabled { get; set; }

    /// <summary>Public values of the type. For Telegram: <c>chatId</c>.</summary>
    public Dictionary<string, string>? Settings { get; set; }

    /// <summary>Secret values of the type. For Telegram: <c>botToken</c>.</summary>
    public Dictionary<string, string>? Secrets { get; set; }
  }

  /// <summary>A target as the API shows it. The secret value is never in it.</summary>
  internal sealed class NotificationTargetView {
    public string Id { get; set; } = string.Empty;

    public string Type { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public bool Enabled { get; set; }

    /// <summary>Public values only.</summary>
    public Dictionary<string, string> Settings { get; set; } = new(StringComparer.Ordinal);

    /// <summary>True when a secret is stored.</summary>
    public bool HasSecret { get; set; }

    /// <summary>A mask and the last 4 characters of the secret, or null when there is no secret.</summary>
    public string? SecretHint { get; set; }

    public DateTimeOffset? CreatedAt { get; set; }

    public DateTimeOffset? UpdatedAt { get; set; }
  }

  /// <summary>Result of a test send. The HTTP call worked, so the status is 200 also for a failed send.</summary>
  internal sealed class NotificationTestResponse {
    public bool Ok { get; set; }

    public string? Reason { get; set; }
  }

  /// <summary>One field of a target type.</summary>
  internal sealed class NotificationFieldView {
    public string Key { get; set; } = string.Empty;

    public string Label { get; set; } = string.Empty;

    public bool Secret { get; set; }

    public bool Required { get; set; }
  }

  /// <summary>A known target type and its fields. The UI builds a form from it.</summary>
  internal sealed class NotificationTypeView {
    public string Type { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    public List<NotificationFieldView> Fields { get; set; } = new();
  }
}
