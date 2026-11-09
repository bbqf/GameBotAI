using System;
using System.Collections.Generic;
using System.Globalization;

namespace GameBot.Domain.Commands;

/// <summary>
/// Rules for a list of image ids in an <c>imageVisible</c> condition and in a WaitForImage detection
/// target (feature 180). One class holds the list logic, so the condition and the step use the same rules.
/// </summary>
public static class ImageListRules {
  /// <summary>The only allowed value of <c>match</c>: true when at least one image of the list matches.</summary>
  public const string MatchAny = "any";

  /// <summary>The largest number of items in a written list. This is the same limit as for a list parameter.</summary>
  public const int MaxItems = 50;

  /// <summary>
  /// Removes empty items and duplicates. The first occurrence of an item stays, and the order is kept.
  /// </summary>
  /// <param name="items">The items to clean. A <c>null</c> value gives an empty list.</param>
  public static IReadOnlyList<string> Normalize(IEnumerable<string>? items) {
    if (items is null) return Array.Empty<string>();
    var seen = new HashSet<string>(StringComparer.Ordinal);
    var result = new List<string>();
    foreach (var item in items) {
      if (string.IsNullOrWhiteSpace(item)) continue;
      var trimmed = item.Trim();
      if (seen.Add(trimmed)) result.Add(trimmed);
    }

    return result;
  }

  /// <summary>True when the condition names an image: one id, or a written list.</summary>
  /// <param name="condition">The condition.</param>
  public static bool HasImage(ImageVisibleStepCondition condition) {
    ArgumentNullException.ThrowIfNull(condition);
    return condition.ImageIds is not null || !string.IsNullOrWhiteSpace(condition.ImageId);
  }

  /// <summary>Adds the written-list errors of an <c>imageVisible</c> condition to <paramref name="errors"/>.</summary>
  /// <param name="condition">The condition.</param>
  /// <param name="fieldPrefix">Text put before each message.</param>
  /// <param name="errors">The error collector.</param>
  public static void AddConditionErrors(ImageVisibleStepCondition condition, string fieldPrefix, ICollection<string> errors) {
    ArgumentNullException.ThrowIfNull(condition);
    ArgumentNullException.ThrowIfNull(errors);
    if (TryValidateWritten(condition.ImageId, condition.ImageIds, condition.Match, fieldPrefix, out var problems)) return;
    foreach (var problem in problems) errors.Add(problem);
  }

  /// <summary>
  /// Checks a written list. A field set means that the value is not null. A single id counts as set when
  /// it is not empty.
  /// </summary>
  /// <param name="imageId">The single id field.</param>
  /// <param name="imageIds">The written list, or <c>null</c> when not set.</param>
  /// <param name="match">The <c>match</c> value, or <c>null</c> when not set.</param>
  /// <param name="fieldPrefix">Text put before each message, for example <c>Step '1' imageVisible</c>.</param>
  /// <param name="errors">The error texts. Empty when the result is true.</param>
  /// <returns><c>true</c> when the list fields are valid.</returns>
  public static bool TryValidateWritten(
      string? imageId,
      IReadOnlyList<string>? imageIds,
      string? match,
      string fieldPrefix,
      out IReadOnlyList<string> errors) {
    var found = new List<string>();
    errors = found;

    if (match is not null && !string.Equals(match, MatchAny, StringComparison.Ordinal)) {
      found.Add($"{fieldPrefix} match must be '{MatchAny}'.");
    }

    if (imageIds is null) return found.Count == 0;

    if (!string.IsNullOrWhiteSpace(imageId)) {
      found.Add($"{fieldPrefix} cannot set both the single image id and imageIds. Use one of them.");
    }

    if (imageIds.Count == 0) {
      found.Add($"{fieldPrefix} imageIds must have at least one item.");
    }
    else if (imageIds.Count > MaxItems) {
      found.Add(string.Format(
        CultureInfo.InvariantCulture,
        "{0} imageIds has {1} items, but allows at most {2}. Remove {3}.",
        fieldPrefix, imageIds.Count, MaxItems, imageIds.Count - MaxItems));
    }

    foreach (var item in imageIds) {
      if (string.IsNullOrWhiteSpace(item)) {
        found.Add($"{fieldPrefix} imageIds must not have an empty item.");
        break;
      }
    }

    return found.Count == 0;
  }
}
