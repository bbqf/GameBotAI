namespace GameBot.Domain.Gamepacks;

/// <summary>
/// The one source of truth for the platform API version (feature 144). This is not the product version.
/// A gamepack manifest field <c>requiredApiMajor</c> must equal <see cref="Major"/>.
/// </summary>
public static class ApiVersion {
  /// <summary>
  /// The API major version. It goes up when a change can make an existing valid sequence or command invalid.
  /// </summary>
  public const int Major = 1;

  /// <summary>The API minor version. It goes up when a change adds a feature and keeps all existing content valid.</summary>
  public const int Minor = 0;

  /// <summary>The API patch version. It goes up for a fix that does not change the contract.</summary>
  public const int Patch = 0;

  /// <summary>The format version of the sequence and command schema. It goes up when the stored format changes.</summary>
  public const int SchemaFormat = 1;
}
