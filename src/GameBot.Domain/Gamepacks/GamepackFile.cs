using GameBot.Domain.Commands;
using GameBot.Domain.QueueSettings;

namespace GameBot.Domain.Gamepacks;

/// <summary>
/// A gamepack file (feature 143): one manifest plus sequences, commands, and images. An empty list is valid.
/// </summary>
public sealed class GamepackFile {
  /// <summary>The manifest. It is required.</summary>
  public GamepackManifest? Manifest { get; init; }

  /// <summary>The sequences of the gamepack. Names are unique without case.</summary>
  public IReadOnlyList<CommandSequence> Sequences { get; init; } = Array.Empty<CommandSequence>();

  /// <summary>The commands of the gamepack. Names are unique without case.</summary>
  public IReadOnlyList<Command> Commands { get; init; } = Array.Empty<Command>();

  /// <summary>The images of the gamepack. Ids are unique without case.</summary>
  public IReadOnlyList<GamepackImage> Images { get; init; } = Array.Empty<GamepackImage>();

  /// <summary>
  /// The seed queue templates (feature 150). The install creates a seed only on a first install, and only when no template
  /// has the same name. A file without this member is valid.
  /// </summary>
  public IReadOnlyList<QueueTemplateDoc> SeedTemplates { get; init; } = Array.Empty<QueueTemplateDoc>();
}

/// <summary>The manifest of a gamepack file.</summary>
public sealed class GamepackManifest {
  /// <summary>The gamepack name. See <see cref="GamepackNameRules"/>.</summary>
  public string? Name { get; init; }

  /// <summary>The version text. No order rule. It must not be empty.</summary>
  public string? Version { get; init; }

  /// <summary>The change text. It is optional.</summary>
  public string? Changelog { get; init; }

  /// <summary>The API major version that the gamepack needs. Stored and shown. Never checked.</summary>
  public int? RequiredApiMajor { get; init; }
}

/// <summary>An image in a gamepack file. The <see cref="Id"/> is the image name (FR-024).</summary>
public sealed class GamepackImage {
  /// <summary>The image id. It is also the image name.</summary>
  public string? Id { get; init; }

  /// <summary>The content type: <c>image/png</c> or <c>image/jpeg</c>.</summary>
  public string? ContentType { get; init; }

  /// <summary>The optional file name.</summary>
  public string? Filename { get; init; }

  /// <summary>The image bytes as base64 text.</summary>
  public string? DataBase64 { get; init; }

  /// <summary>
  /// The ids of the alternate images (feature 153). Null means the member is absent: an install keeps the stored list.
  /// An empty list clears the stored list.
  /// </summary>
  [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
  public IReadOnlyList<string>? Alternates { get; init; }
}
