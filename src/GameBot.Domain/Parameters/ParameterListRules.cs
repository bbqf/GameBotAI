using System;
using System.Collections.Generic;
using System.Linq;

namespace GameBot.Domain.Parameters;

/// <summary>
/// Helpers for list-valued parameters (feature 179). A list is stored as an ordered list of unique
/// items in <see cref="ParameterBinding.Values"/>. The same list is also stored as joined text in
/// <see cref="ParameterBinding.Value"/>, so the text-based scope keeps working.
/// </summary>
public static class ParameterListRules {
  /// <summary>The character that joins the items of a list into text. No item may contain it.</summary>
  public const char Separator = '|';

  /// <summary>
  /// Removes empty items and duplicates. The first occurrence of an item stays, and the order is kept.
  /// </summary>
  /// <param name="items">The items to clean. A <c>null</c> value gives an empty list.</param>
  public static IReadOnlyList<string> Normalize(IEnumerable<string>? items) {
    if (items is null) return Array.Empty<string>();
    var seen = new HashSet<string>(StringComparer.Ordinal);
    var result = new List<string>();
    foreach (var item in items) {
      if (string.IsNullOrEmpty(item)) continue;
      if (seen.Add(item)) result.Add(item);
    }

    return result;
  }

  /// <summary>Joins the items of a list into one text with <see cref="Separator"/>.</summary>
  /// <param name="items">The items to join.</param>
  public static string Join(IEnumerable<string> items) {
    ArgumentNullException.ThrowIfNull(items);
    return string.Join(Separator, items);
  }

  /// <summary>
  /// Splits a joined text into its items. An empty or <c>null</c> text gives an empty list. Empty
  /// items are dropped.
  /// </summary>
  /// <param name="text">The joined text.</param>
  public static IReadOnlyList<string> Split(string? text) {
    if (string.IsNullOrEmpty(text)) return Array.Empty<string>();
    return text.Split(Separator, StringSplitOptions.RemoveEmptyEntries);
  }

  /// <summary>Counts the items in a joined text.</summary>
  /// <param name="text">The joined text.</param>
  public static int Count(string? text) => Split(text).Count;

  /// <summary>
  /// Reads a binding as a list. Returns <see cref="ParameterBinding.Values"/> when set. Otherwise, for a
  /// multi-value declaration, a scalar value reads as a list of one item. Otherwise <c>null</c>.
  /// </summary>
  /// <param name="declaration">The declaration of the parameter, or <c>null</c> when unknown.</param>
  /// <param name="binding">The binding to read.</param>
  public static IReadOnlyList<string>? ReadAsList(ParameterDeclaration? declaration, ParameterBinding binding) {
    ArgumentNullException.ThrowIfNull(binding);
    if (binding.Values is not null) return binding.Values;
    if (declaration is { Multiple: true } && binding.Value is not null) return new[] { binding.Value };
    return null;
  }

  /// <summary>
  /// Checks the number of items against the limits of a declaration. Returns a message with a hint when
  /// the count is outside the limits, otherwise <c>null</c>.
  /// </summary>
  /// <param name="declaration">The declaration with the limits.</param>
  /// <param name="count">The number of items in the list.</param>
  public static string? CheckLimits(ParameterDeclaration declaration, int count) {
    ArgumentNullException.ThrowIfNull(declaration);
    var name = declaration.Name;
    if (declaration.MinItems is { } min && count < min) {
      return $"parameter '{name}' has {count} items, but needs at least {min}. Select {min - count} more.";
    }

    if (declaration.MaxItems is { } max && count > max) {
      return $"parameter '{name}' has {count} items, but allows at most {max}. Remove {count - max}.";
    }

    return null;
  }
}
