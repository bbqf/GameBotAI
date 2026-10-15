namespace GameBot.Domain.QueueSettings;

/// <summary>
/// Case-insensitive maps from a name to the ids of the target instance, one map for each object type (feature 145).
/// More than one id can have the same name. The first id is the main id.
/// </summary>
public sealed class NameMap {
  private readonly Dictionary<string, Dictionary<string, List<string>>> _byType = new(StringComparer.Ordinal);
  private readonly Dictionary<string, Dictionary<string, string>> _nameById = new(StringComparer.Ordinal);

  /// <summary>Adds an object.</summary>
  /// <param name="type">One of <see cref="SettingsObjectTypes"/>.</param>
  /// <param name="name">The object name.</param>
  /// <param name="id">The id of the object on this instance.</param>
  public void Add(string type, string name, string id) {
    ArgumentNullException.ThrowIfNull(type);
    ArgumentNullException.ThrowIfNull(name);
    ArgumentNullException.ThrowIfNull(id);
    if (!_byType.TryGetValue(type, out var names)) {
      names = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
      _byType[type] = names;
      _nameById[type] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    }

    if (!names.TryGetValue(name, out var ids)) {
      ids = new List<string>();
      names[name] = ids;
    }

    if (!ids.Contains(id, StringComparer.OrdinalIgnoreCase)) {
      ids.Add(id);
    }

    _nameById[type].TryAdd(id, name);
  }

  /// <summary>Gets all ids with this name. The list is empty when no object has the name.</summary>
  /// <param name="type">One of <see cref="SettingsObjectTypes"/>.</param>
  /// <param name="name">The object name.</param>
  public IReadOnlyList<string> Ids(string type, string name) {
    ArgumentNullException.ThrowIfNull(type);
    ArgumentNullException.ThrowIfNull(name);
    return _byType.TryGetValue(type, out var names) && names.TryGetValue(name, out var ids)
      ? ids
      : Array.Empty<string>();
  }

  /// <summary>Gets the main id of a name.</summary>
  /// <param name="type">One of <see cref="SettingsObjectTypes"/>.</param>
  /// <param name="name">The object name.</param>
  /// <param name="id">The id, when found.</param>
  public bool TryGetId(string type, string name, out string id) {
    var ids = Ids(type, name);
    if (ids.Count > 0) {
      id = ids[0];
      return true;
    }

    id = string.Empty;
    return false;
  }

  /// <summary>Gets the name of an id, or null.</summary>
  /// <param name="type">One of <see cref="SettingsObjectTypes"/>.</param>
  /// <param name="id">The id.</param>
  public string? NameOf(string type, string id) {
    ArgumentNullException.ThrowIfNull(type);
    ArgumentNullException.ThrowIfNull(id);
    return _nameById.TryGetValue(type, out var names) && names.TryGetValue(id, out var name) ? name : null;
  }
}
