using System.Collections;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace GameBot.Domain.Gamepacks;

/// <summary>The reason codes of a type member problem.</summary>
internal enum GamepackTypeProblemReason {
  Missing,
  Unknown,
  NotString,
  Duplicate
}

/// <summary>A problem with the type member of one polymorphic object: where it is and what is wrong.</summary>
/// <param name="Path">The JSON path of the object.</param>
/// <param name="Reason">The reason code.</param>
internal sealed record GamepackTypeProblem(string Path, GamepackTypeProblemReason Reason) {
  /// <summary>Short text for the reason.</summary>
  public string ReasonText => Reason switch {
    GamepackTypeProblemReason.Missing => "the type member is missing",
    GamepackTypeProblemReason.Unknown => "the type value is unknown",
    GamepackTypeProblemReason.NotString => "the type value must be a string",
    _ => "the type member is duplicated"
  };
}

/// <summary>
/// Checks the type member (the discriminator) of each polymorphic object in a JSON entry before the typed read
/// (feature 154). The check reads the polymorphism data of the type metadata, so a new polymorphic type needs no
/// code change here. It never reads the text of an exception.
/// </summary>
internal static class GamepackTypeDiscriminatorCheck {
  /// <summary>Finds the first type member problem in an entry.</summary>
  /// <param name="element">The JSON of the entry.</param>
  /// <param name="target">The type that the entry is read as.</param>
  /// <param name="options">The JSON options of the read.</param>
  /// <param name="path">The JSON path of the entry.</param>
  /// <returns>The first problem, or null when the type members are valid.</returns>
  public static GamepackTypeProblem? Find(JsonElement element, Type target, JsonSerializerOptions options, string path) {
    ArgumentNullException.ThrowIfNull(target);
    ArgumentNullException.ThrowIfNull(options);
    ArgumentNullException.ThrowIfNull(path);
    return Walk(element, target, options, path, 0);
  }

  private const int MaxDepth = 64;

  private static GamepackTypeProblem? Walk(JsonElement element, Type type, JsonSerializerOptions options, string path, int depth) {
    if (depth > MaxDepth || element.ValueKind == JsonValueKind.Null) {
      return null;
    }

    type = Nullable.GetUnderlyingType(type) ?? type;
    if (type == typeof(string) || type.IsPrimitive || type.IsEnum || type == typeof(object) || type == typeof(JsonElement)) {
      return null;
    }

    JsonTypeInfo info;
    try {
      info = options.GetTypeInfo(type);
    }
    catch (InvalidOperationException) {
      return null;
    }
    catch (NotSupportedException) {
      return null;
    }

    switch (element.ValueKind) {
      case JsonValueKind.Array:
        return WalkArray(element, type, options, path, depth);
      case JsonValueKind.Object:
        return WalkObject(element, type, info, options, path, depth);
      default:
        return null;
    }
  }

  private static GamepackTypeProblem? WalkArray(JsonElement element, Type type, JsonSerializerOptions options, string path, int depth) {
    var itemType = ItemTypeOf(type);
    if (itemType is null) {
      return null;
    }

    var index = 0;
    foreach (var item in element.EnumerateArray()) {
      var problem = Walk(item, itemType, options, $"{path}[{index}]", depth + 1);
      if (problem is not null) {
        return problem;
      }

      index++;
    }

    return null;
  }

  private static GamepackTypeProblem? WalkObject(JsonElement element, Type type, JsonTypeInfo info, JsonSerializerOptions options, string path, int depth) {
    var valueType = DictionaryValueTypeOf(type);
    if (valueType is not null) {
      foreach (var member in element.EnumerateObject()) {
        var problem = Walk(member.Value, valueType, options, $"{path}.{member.Name}", depth + 1);
        if (problem is not null) {
          return problem;
        }
      }

      return null;
    }

    var polymorphism = info.PolymorphismOptions;
    if (polymorphism is not null) {
      var problem = CheckDiscriminator(element, type, polymorphism, path, out var derived);
      if (problem is not null) {
        return problem;
      }

      if (derived is not null && derived != type) {
        try {
          info = options.GetTypeInfo(derived);
        }
        catch (InvalidOperationException) {
          return null;
        }

        type = derived;
      }
    }

    if (info.Kind != JsonTypeInfoKind.Object) {
      return null;
    }

    foreach (var property in info.Properties) {
      foreach (var member in element.EnumerateObject()) {
        if (!NameMatches(property.Name, member.Name, options.PropertyNameCaseInsensitive)) {
          continue;
        }

        var problem = Walk(member.Value, property.PropertyType, options, $"{path}.{member.Name}", depth + 1);
        if (problem is not null) {
          return problem;
        }
      }
    }

    return null;
  }

  private static GamepackTypeProblem? CheckDiscriminator(JsonElement element, Type baseType, JsonPolymorphismOptions polymorphism, string path, out Type? derived) {
    derived = null;
    var name = polymorphism.TypeDiscriminatorPropertyName;
    var values = new List<JsonElement>();
    foreach (var member in element.EnumerateObject()) {
      if (string.Equals(member.Name, name, StringComparison.Ordinal)) {
        values.Add(member.Value);
      }
    }

    if (values.Count == 0) {
      // A base type that is not abstract can be read without a discriminator.
      return baseType.IsAbstract || baseType.IsInterface ? new GamepackTypeProblem(path, GamepackTypeProblemReason.Missing) : null;
    }

    if (values.Count > 1) {
      return new GamepackTypeProblem(path, GamepackTypeProblemReason.Duplicate);
    }

    var value = values[0];
    if (value.ValueKind != JsonValueKind.String && value.ValueKind != JsonValueKind.Number) {
      return new GamepackTypeProblem(path, GamepackTypeProblemReason.NotString);
    }

    foreach (var candidate in polymorphism.DerivedTypes) {
      if (candidate.TypeDiscriminator is string text) {
        if (value.ValueKind == JsonValueKind.String && string.Equals(value.GetString(), text, StringComparison.Ordinal)) {
          derived = candidate.DerivedType;
          return null;
        }
      }
      else if (candidate.TypeDiscriminator is int number
          && value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var parsed) && parsed == number) {
        derived = candidate.DerivedType;
        return null;
      }
    }

    // A number is a wrong kind when every discriminator is a string.
    var anyNumeric = polymorphism.DerivedTypes.Any(d => d.TypeDiscriminator is int);
    if (value.ValueKind == JsonValueKind.Number && !anyNumeric) {
      return new GamepackTypeProblem(path, GamepackTypeProblemReason.NotString);
    }

    return new GamepackTypeProblem(path, GamepackTypeProblemReason.Unknown);
  }

  private static bool NameMatches(string propertyName, string memberName, bool ignoreCase) =>
    string.Equals(propertyName, memberName, ignoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

  private static Type? ItemTypeOf(Type type) {
    if (type.IsArray) {
      return type.GetElementType();
    }

    foreach (var candidate in type.GetInterfaces().Prepend(type)) {
      if (candidate.IsGenericType && candidate.GetGenericTypeDefinition() == typeof(IEnumerable<>)) {
        return candidate.GetGenericArguments()[0];
      }
    }

    return null;
  }

  private static Type? DictionaryValueTypeOf(Type type) {
    foreach (var candidate in type.GetInterfaces().Prepend(type)) {
      if (candidate.IsGenericType) {
        var definition = candidate.GetGenericTypeDefinition();
        if (definition == typeof(IDictionary<,>) || definition == typeof(IReadOnlyDictionary<,>)) {
          return candidate.GetGenericArguments()[1];
        }
      }
    }

    return typeof(IDictionary).IsAssignableFrom(type) ? typeof(object) : null;
  }
}
