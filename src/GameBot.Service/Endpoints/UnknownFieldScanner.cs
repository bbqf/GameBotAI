using System.Collections;
using System.Collections.Concurrent;
using System.Reflection;
using System.Text.Json;

namespace GameBot.Service.Endpoints;

/// <summary>
/// Feature 135: finds the fields of a JSON body that the target DTO type does not have. The DTO types
/// are the source of the published schema, so this check cannot drift from the schema.
/// </summary>
internal static class UnknownFieldScanner {
  private static readonly ConcurrentDictionary<(Type, string), PropertyInfo?> PropertyCache = new();

  /// <summary>
  /// Returns the full path of each unknown field, in body order, for example
  /// <c>steps[0].primitiveTap.bogusField</c>. The result is empty when all fields are known.
  /// </summary>
  public static IReadOnlyList<string> Find(JsonElement root, Type dtoType) {
    ArgumentNullException.ThrowIfNull(dtoType);
    var found = new List<string>();
    Walk(root, dtoType, string.Empty, found);
    return found;
  }

  private static void Walk(JsonElement element, Type type, string path, List<string> found) {
    switch (element.ValueKind) {
      case JsonValueKind.Object:
        WalkObject(element, type, path, found);
        break;
      case JsonValueKind.Array:
        WalkArray(element, type, path, found);
        break;
      default:
        break;
    }
  }

  private static void WalkObject(JsonElement element, Type type, string path, List<string> found) {
    type = Nullable.GetUnderlyingType(type) ?? type;
    // A dictionary holds data: its keys are not field names.
    if (IsDictionary(type) || !IsDtoType(type)) return;

    foreach (var member in element.EnumerateObject()) {
      var memberPath = path.Length == 0 ? member.Name : $"{path}.{member.Name}";
      var property = FindProperty(type, member.Name);
      if (property is null) {
        found.Add(memberPath);
        continue;
      }
      Walk(member.Value, property.PropertyType, memberPath, found);
    }
  }

  private static void WalkArray(JsonElement element, Type type, string path, List<string> found) {
    var itemType = GetItemType(type);
    if (itemType is null) return;
    var index = 0;
    foreach (var item in element.EnumerateArray()) {
      Walk(item, itemType, $"{path}[{index}]", found);
      index++;
    }
  }

  private static PropertyInfo? FindProperty(Type type, string name) =>
    PropertyCache.GetOrAdd((type, name), key => {
      // A property with [JsonIgnore] is not in the schema, so a body must not set it.
      foreach (var p in key.Item1.GetProperties(BindingFlags.Public | BindingFlags.Instance)) {
        if (!string.Equals(p.Name, key.Item2, StringComparison.OrdinalIgnoreCase)) continue;
        // A condition such as WhenWritingNull only changes output, so the field stays readable.
        var ignore = p.GetCustomAttribute<System.Text.Json.Serialization.JsonIgnoreAttribute>();
        if (ignore is { Condition: System.Text.Json.Serialization.JsonIgnoreCondition.Always }) return null;
        return p;
      }
      return null;
    });

  private static bool IsDictionary(Type type) =>
    type.IsGenericType && type.GetGenericTypeDefinition() is var d
      && (d == typeof(Dictionary<,>) || d == typeof(IDictionary<,>) || d == typeof(IReadOnlyDictionary<,>));

  private static bool IsDtoType(Type type) =>
    type.IsClass && type != typeof(string) && !typeof(IEnumerable).IsAssignableFrom(type);

  private static Type? GetItemType(Type type) {
    type = Nullable.GetUnderlyingType(type) ?? type;
    if (type.IsArray) return type.GetElementType();
    if (type == typeof(string) || IsDictionary(type)) return null;
    foreach (var candidate in new[] { type }.Concat(type.GetInterfaces())) {
      if (candidate.IsGenericType && candidate.GetGenericTypeDefinition() == typeof(IEnumerable<>)) {
        return candidate.GetGenericArguments()[0];
      }
    }
    return null;
  }
}
