using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using GameBot.Domain.Parameters;
using GameBot.Domain.Queues;
using GameBot.Domain.QueueTemplates;

namespace GameBot.Domain.QueueSettings;

/// <summary>The type names that the settings document uses in references and in the import report.</summary>
public static class SettingsObjectTypes {
  /// <summary>A sequence.</summary>
  public const string Sequence = "sequence";

  /// <summary>A command.</summary>
  public const string Command = "command";

  /// <summary>An image. The name is the image id.</summary>
  public const string Image = "image";

  /// <summary>A queue template.</summary>
  public const string QueueTemplate = "queueTemplate";

  /// <summary>A game.</summary>
  public const string Game = "game";

  /// <summary>A queue.</summary>
  public const string Queue = "queue";
}

/// <summary>
/// The settings document (feature 145). It is the body of the export and of the import. It has no id and no time
/// stamp that the instance assigned (FR-004). All links use names. A sequence and a command are JSON objects with the
/// shape of the stored object, without id and time stamps.
/// </summary>
public sealed class SettingsDocument {
  /// <summary>The only supported format version.</summary>
  public const int CurrentFormatVersion = 1;

  /// <summary>The format version. The value is 1.</summary>
  public int FormatVersion { get; init; } = CurrentFormatVersion;

  /// <summary>The time of the export. The import ignores it.</summary>
  public DateTimeOffset? ExportedAtUtc { get; init; }

  /// <summary>The settings of the queues. Names are unique without case.</summary>
  public IReadOnlyList<QueueSettingsDoc> Queues { get; init; } = Array.Empty<QueueSettingsDoc>();

  /// <summary>The queue templates. Names are unique without case.</summary>
  public IReadOnlyList<QueueTemplateDoc> QueueTemplates { get; init; } = Array.Empty<QueueTemplateDoc>();

  /// <summary>The sequences that no gamepack owns. Names are unique without case.</summary>
  public IReadOnlyList<JsonObject> Sequences { get; init; } = Array.Empty<JsonObject>();

  /// <summary>The commands that no gamepack owns. Names are unique without case.</summary>
  public IReadOnlyList<JsonObject> Commands { get; init; } = Array.Empty<JsonObject>();

  /// <summary>The uses of objects that the document does not carry. The import uses it for report text only.</summary>
  public IReadOnlyList<NameReference> References { get; init; } = Array.Empty<NameReference>();
}

/// <summary>The settings of one queue. The key is <see cref="Name"/>.</summary>
public sealed class QueueSettingsDoc {
  /// <summary>The queue name.</summary>
  public string Name { get; init; } = string.Empty;

  /// <summary>The device serial. It is machine-specific. The import takes it as given.</summary>
  public string EmulatorSerial { get; init; } = string.Empty;

  /// <summary>The emulator instance name. It is machine-specific.</summary>
  public string? EmulatorInstanceName { get; init; }

  /// <summary>The emulator instance index. It is machine-specific.</summary>
  public int? EmulatorInstanceIndex { get; init; }

  /// <summary>The stored cycle flag.</summary>
  public bool CycleExecution { get; init; }

  /// <summary>True to park the emulator at the launcher in idle gaps.</summary>
  public bool PauseWhenIdle { get; init; }

  /// <summary>The idle gap in seconds.</summary>
  public int IdleThresholdSeconds { get; init; } = 30;

  /// <summary>True to start the queue again after a service restart.</summary>
  public bool ResumeOnServiceStart { get; init; }

  /// <summary>The notification level.</summary>
  public GameBot.Domain.Notifications.NotificationLevel NotificationLevel { get; init; }

  /// <summary>The failure policy, or null.</summary>
  public QueueFailurePolicy? FailurePolicy { get; init; }

  /// <summary>The device recovery, or null.</summary>
  public QueueDeviceRecovery? DeviceRecovery { get; init; }

  /// <summary>The name of the linked queue template, or null.</summary>
  public string? LinkedTemplate { get; init; }

  /// <summary>The name of the linked game, or null. Games are not exported.</summary>
  public string? LinkedGame { get; init; }

  /// <summary>The sequence names of the queue entries, in order.</summary>
  public IReadOnlyList<string> Entries { get; init; } = Array.Empty<string>();
}

/// <summary>A queue template in the document. The key is <see cref="Name"/>.</summary>
public sealed class QueueTemplateDoc {
  /// <summary>The template name.</summary>
  public string Name { get; init; } = string.Empty;

  /// <summary>The entries, in run order.</summary>
  public IReadOnlyList<QueueTemplateEntryDoc> Entries { get; init; } = Array.Empty<QueueTemplateEntryDoc>();
}

/// <summary>One entry of a queue template in the document.</summary>
public sealed class QueueTemplateEntryDoc {
  /// <summary>The sequence name. It can name a sequence that a gamepack owns.</summary>
  public string Sequence { get; init; } = string.Empty;

  /// <summary>False when the entry is switched off.</summary>
  public bool Enabled { get; init; } = true;

  /// <summary>The schedule type.</summary>
  public ScheduleType ScheduleType { get; init; } = ScheduleType.OncePerRun;

  /// <summary>The time of day of a timer entry, or null.</summary>
  public TimeOnly? TimerTimeOfDay { get; init; }

  /// <summary>The relative offset of a timer entry, or null.</summary>
  public TimeSpan? TimerRelativeOffset { get; init; }

  /// <summary>The parameter values of the entry.</summary>
  public IReadOnlyList<ParameterBinding> ParameterValues { get; init; } = Array.Empty<ParameterBinding>();
}

/// <summary>A use of an object by name, with no content.</summary>
public sealed class NameReference {
  /// <summary>One of <see cref="SettingsObjectTypes"/>.</summary>
  public string Type { get; init; } = string.Empty;

  /// <summary>The object name. For an image, the id.</summary>
  public string Name { get; init; } = string.Empty;

  /// <summary>The gamepack name when a gamepack owns the object, or null.</summary>
  public string? Gamepack { get; init; }

  /// <summary>The gamepack version when a gamepack owns the object, or null.</summary>
  public string? Version { get; init; }

  /// <summary>The user of the object as <c>type:name</c>, for example <c>queueTemplate:Daily</c>.</summary>
  public string UsedBy { get; init; } = string.Empty;
}

/// <summary>Reads and writes the settings document as JSON.</summary>
public static class SettingsDocumentJson {
  private static readonly JsonSerializerOptions ReadOptions = BuildOptions(indented: false);
  private static readonly JsonSerializerOptions WriteOptions = BuildOptions(indented: true);

  /// <summary>The options for the document. Member names are camel case. An enum is a name or a number.</summary>
  public static JsonSerializerOptions Options => ReadOptions;

  /// <summary>Writes a document as indented JSON.</summary>
  /// <param name="document">The document.</param>
  public static string Serialize(SettingsDocument document) {
    ArgumentNullException.ThrowIfNull(document);
    return JsonSerializer.Serialize(document, WriteOptions);
  }

  private static JsonSerializerOptions BuildOptions(bool indented) {
    var options = new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = indented };
    options.Converters.Add(new JsonStringEnumConverter());
    return options;
  }
}
