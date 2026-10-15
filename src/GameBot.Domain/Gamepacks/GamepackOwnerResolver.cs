namespace GameBot.Domain.Gamepacks;

/// <summary>The owner of an object. A null <see cref="Name"/> means the user.</summary>
/// <param name="Name">The gamepack name, or null for the user.</param>
/// <param name="Version">The gamepack version, or null for the user.</param>
public sealed record GamepackOwner(string? Name, string? Version) {
  /// <summary>The owner when no gamepack owns the object (FR-005).</summary>
  public static GamepackOwner User { get; } = new(null, null);

  /// <summary>True when the user owns the object.</summary>
  public bool IsUser => Name is null;

  /// <summary>The text form for a problem report: <c>user</c> or <c>&lt;name&gt; &lt;version&gt;</c>.</summary>
  public string ToText() => IsUser ? "user" : Name + " " + Version;
}

/// <summary>
/// Reads the owner of an object from a registry snapshot. The owner is found by type and stored id, so a hand
/// rename does not drop the owner (FR-015). The caller reads an object that exists, so an owner entry without an
/// object is never shown (FR-005).
/// </summary>
public sealed class GamepackOwnerResolver {
  private readonly Dictionary<string, OwnerEntry> _byTypeAndId = new(StringComparer.OrdinalIgnoreCase);

  /// <summary>Creates a resolver for a registry snapshot.</summary>
  /// <param name="registry">The registry snapshot.</param>
  public GamepackOwnerResolver(GamepackRegistry registry) {
    ArgumentNullException.ThrowIfNull(registry);
    foreach (var pair in registry.Owners) {
      _byTypeAndId[IdKey(GamepackRegistry.TypeOfKey(pair.Key), pair.Value.Id)] = pair.Value;
    }
  }

  /// <summary>Gets the owner of an existing object.</summary>
  /// <param name="type">One of <see cref="GamepackObjectTypes"/>.</param>
  /// <param name="id">The stored id of the object.</param>
  public GamepackOwner Resolve(string type, string id) {
    ArgumentNullException.ThrowIfNull(type);
    ArgumentNullException.ThrowIfNull(id);
    return _byTypeAndId.TryGetValue(IdKey(type, id), out var entry)
      ? new GamepackOwner(entry.Gamepack, entry.Version)
      : GamepackOwner.User;
  }

  /// <summary>Gets the owner record of an object, or null when the user owns it.</summary>
  /// <param name="type">One of <see cref="GamepackObjectTypes"/>.</param>
  /// <param name="id">The stored id of the object.</param>
  public OwnerEntry? Find(string type, string id) {
    ArgumentNullException.ThrowIfNull(type);
    ArgumentNullException.ThrowIfNull(id);
    return _byTypeAndId.TryGetValue(IdKey(type, id), out var entry) ? entry : null;
  }

  private static string IdKey(string type, string id) => type + "|" + id;
}
