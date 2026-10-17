using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.Json.Serialization;
using FluentAssertions;
using GameBot.Domain.Commands;
using Xunit;

#pragma warning disable CA1707, CA2007, CA1861

namespace GameBot.UnitTests.Gamepacks;

/// <summary>
/// Feature 154 (FR-006): a polymorphic type in the type graph of a sequence or a command must be in the list of this
/// test. A new polymorphic type fails the test, so its member order tests are not forgotten.
/// </summary>
public sealed class GamepackPolymorphicTypesReflectionTests {
  [Fact]
  public void TheGraphOfSequenceAndCommandHoldsOnlyTheKnownPolymorphicTypes() {
    var found = new HashSet<Type>();
    var seen = new HashSet<Type>();
    Walk(typeof(CommandSequence), found, seen);
    Walk(typeof(Command), found, seen);

    var known = new HashSet<Type> { typeof(SequenceStepCondition), typeof(LoopConfig) };
    var unlisted = found.Except(known).Select(t => t.FullName).ToList();
    unlisted.Should().BeEmpty("a polymorphic type that is not in the list needs member order tests: " + string.Join(", ", unlisted));
    found.Should().BeEquivalentTo(known);
  }

  private static bool IsSystemType(Type type) =>
    type.Namespace?.StartsWith("System", StringComparison.Ordinal) == true;

  private static void Walk(Type type, HashSet<Type> found, HashSet<Type> seen) {
    type = Nullable.GetUnderlyingType(type) ?? type;
    if (type.IsPrimitive || type.IsEnum || type == typeof(string) || type == typeof(object) || !seen.Add(type)) {
      return;
    }

    if (type.IsArray) {
      Walk(type.GetElementType()!, found, seen);
      return;
    }

    if (type.IsGenericType) {
      foreach (var argument in type.GetGenericArguments()) {
        Walk(argument, found, seen);
      }
    }

    if (IsSystemType(type)) {
      return;
    }

    if (type.GetCustomAttribute<JsonPolymorphicAttribute>() is not null) {
      found.Add(type);
    }

    foreach (var derived in type.GetCustomAttributes<JsonDerivedTypeAttribute>()) {
      Walk(derived.DerivedType, found, seen);
    }

    foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance)) {
      if (property.GetCustomAttribute<JsonIgnoreAttribute>() is { Condition: JsonIgnoreCondition.Always }) {
        continue;
      }

      Walk(property.PropertyType, found, seen);
    }
  }
}
