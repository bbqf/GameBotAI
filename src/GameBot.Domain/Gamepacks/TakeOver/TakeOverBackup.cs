using System.Text.Json;

namespace GameBot.Domain.Gamepacks;

/// <summary>The old content of an image in a take-over backup.</summary>
public sealed class TakeOverBackupImage {
  /// <summary>The content type.</summary>
  public string ContentType { get; set; } = string.Empty;

  /// <summary>The file name, or null.</summary>
  public string? Filename { get; set; }

  /// <summary>The image bytes as base64 text.</summary>
  public string DataBase64 { get; set; } = string.Empty;
}

/// <summary>One replaced object in a take-over backup (feature 149).</summary>
public sealed class TakeOverBackupEntry {
  /// <summary>One of <see cref="GamepackObjectTypes"/>: sequence, command, or image.</summary>
  public string Type { get; set; } = string.Empty;

  /// <summary>The object name. For an image, the id.</summary>
  public string Name { get; set; } = string.Empty;

  /// <summary>The stored id. It does not change in the take-over.</summary>
  public string ObjectId { get; set; } = string.Empty;

  /// <summary>The old sequence as JSON. Set for the type sequence.</summary>
  public JsonElement? Sequence { get; set; }

  /// <summary>The old command as JSON. Set for the type command.</summary>
  public JsonElement? Command { get; set; }

  /// <summary>The old image. Set for the type image.</summary>
  public TakeOverBackupImage? Image { get; set; }

  /// <summary>SHA-256 (lower-case hex) of the canonical content that the install wrote.</summary>
  public string PostHash { get; set; } = string.Empty;
}

/// <summary>
/// The backup of the objects that one install replaced (feature 149). The file is
/// <c>gamepacks\takeover-backups\&lt;id&gt;.json</c>.
/// </summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "CA2227:Collection properties should be read only", Justification = "Backup data model read by System.Text.Json.")]
[System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1002:Do not expose generic lists", Justification = "Backup data model read by System.Text.Json.")]
public sealed class TakeOverBackup {
  /// <summary>The backup id. The store sets it on write. It equals the file name without the extension.</summary>
  public string Id { get; set; } = string.Empty;

  /// <summary>The time of the install.</summary>
  public DateTimeOffset CreatedAtUtc { get; set; }

  /// <summary>The name of the gamepack that took the objects.</summary>
  public string Gamepack { get; set; } = string.Empty;

  /// <summary>The gamepack version at the install.</summary>
  public string Version { get; set; } = string.Empty;

  /// <summary>One entry for each replaced object. At least one.</summary>
  public List<TakeOverBackupEntry> Entries { get; set; } = new();
}
