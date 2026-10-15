namespace GameBot.Domain.Gamepacks;

/// <summary>An object that a gamepack version owns.</summary>
public sealed class GamepackObjectRef {
  /// <summary>One of <see cref="GamepackObjectTypes"/> (sequence, command, or image).</summary>
  public string Type { get; set; } = string.Empty;

  /// <summary>The object name. For an image, the name is the id.</summary>
  public string Name { get; set; } = string.Empty;

  /// <summary>The stored id of the object.</summary>
  public string Id { get; set; } = string.Empty;
}

/// <summary>The kept previous version of a gamepack. The content is in <c>gamepacks/previous/&lt;name&gt;.json</c>.</summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "CA2227:Collection properties should be read only", Justification = "Registry data model read by System.Text.Json.")]
[System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1002:Do not expose generic lists", Justification = "Registry data model read by System.Text.Json.")]
public sealed class PreviousVersion {
  /// <summary>The version text.</summary>
  public string Version { get; set; } = string.Empty;

  /// <summary>The change text.</summary>
  public string Changelog { get; set; } = string.Empty;

  /// <summary>The optional API major version.</summary>
  public int? RequiredApiMajor { get; set; }

  /// <summary>The time at which this version was installed.</summary>
  public DateTimeOffset InstalledAtUtc { get; set; }

  /// <summary>The objects that this version owned.</summary>
  public List<GamepackObjectRef> Objects { get; set; } = new();
}

/// <summary>An installed gamepack.</summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "CA2227:Collection properties should be read only", Justification = "Registry data model read by System.Text.Json.")]
[System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1002:Do not expose generic lists", Justification = "Registry data model read by System.Text.Json.")]
public sealed class InstalledGamepack {
  /// <summary>The gamepack name.</summary>
  public string Name { get; set; } = string.Empty;

  /// <summary>The version text.</summary>
  public string Version { get; set; } = string.Empty;

  /// <summary>The change text.</summary>
  public string Changelog { get; set; } = string.Empty;

  /// <summary>The optional API major version.</summary>
  public int? RequiredApiMajor { get; set; }

  /// <summary>The time of the install.</summary>
  public DateTimeOffset InstalledAtUtc { get; set; }

  /// <summary>The objects that this version owns.</summary>
  public List<GamepackObjectRef> Objects { get; set; } = new();

  /// <summary>The kept previous version, or null. At most one is kept.</summary>
  public PreviousVersion? Previous { get; set; }
}

/// <summary>The owner record of one object.</summary>
public sealed class OwnerEntry {
  /// <summary>The gamepack name.</summary>
  public string Gamepack { get; set; } = string.Empty;

  /// <summary>The gamepack version.</summary>
  public string Version { get; set; } = string.Empty;

  /// <summary>The stored id of the owned object.</summary>
  public string Id { get; set; } = string.Empty;
}

/// <summary>
/// The registry (<c>gamepacks/registry.json</c>). An object with no owner entry belongs to the user (FR-005).
/// </summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "CA2227:Collection properties should be read only", Justification = "Registry data model read by System.Text.Json.")]
[System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1002:Do not expose generic lists", Justification = "Registry data model read by System.Text.Json.")]
public sealed class GamepackRegistry {
  /// <summary>Installed gamepacks. The key is the lower-case name.</summary>
  public Dictionary<string, InstalledGamepack> Gamepacks { get; set; } = new(StringComparer.Ordinal);

  /// <summary>Owner entries. The key is <c>type:name</c> in lower case.</summary>
  public Dictionary<string, OwnerEntry> Owners { get; set; } = new(StringComparer.Ordinal);

  /// <summary>Builds the owner key of an object.</summary>
  /// <param name="type">One of <see cref="GamepackObjectTypes"/>.</param>
  /// <param name="name">The object name.</param>
  [System.Diagnostics.CodeAnalysis.SuppressMessage("Globalization", "CA1308:Normalize strings to uppercase", Justification = "The key is the lower-case name, as the data model says. It is never shown as text.")]
  public static string ObjectKey(string type, string name) {
    ArgumentNullException.ThrowIfNull(type);
    ArgumentNullException.ThrowIfNull(name);
    return type.ToLowerInvariant() + ":" + name.ToLowerInvariant();
  }

  /// <summary>Gets the type part of an owner key.</summary>
  /// <param name="key">An owner key.</param>
  public static string TypeOfKey(string key) {
    ArgumentNullException.ThrowIfNull(key);
    var colon = key.IndexOf(':', StringComparison.Ordinal);
    return colon < 0 ? key : key[..colon];
  }
}
