using System.Collections.Generic;
using System.Text.Json;
using System.Text.RegularExpressions;
using GameBot.Domain.Commands;

namespace GameBot.Domain.Utils;

/// <summary>
/// Substitutes <c>{{key}}</c> template placeholders in strings and action payload parameters.
/// <para>
/// Keys are dotted identifiers (<c>\w+(?:\.\w+)*</c>), which covers both the original loop
/// <c>{{iteration}}</c> key and the reserved <c>{{queue.emulatorSerial}}</c>-style built-ins added by
/// feature 078. The pattern is a strict superset of the original <c>\w+</c>, so every placeholder
/// that matched before still matches.
/// </para>
/// <para>
/// <see cref="Substitute"/> and <see cref="SubstitutePayload"/> are <em>lenient</em>: unknown keys are
/// left in place, which the loop path depends on so an outer context can resolve them later. Use
/// <see cref="TrySubstitute"/> when an unresolved key must be reported instead of passed through.
/// </para>
/// </summary>
public static class TemplateSubstitutor {
  private static readonly Regex PlaceholderPattern =
      new(@"\{\{(\w+(?:\.\w+)*)\}\}", RegexOptions.Compiled, TimeSpan.FromMilliseconds(500));

  /// <summary>
  /// Returns every distinct <c>{{key}}</c> name referenced by <paramref name="template"/>, in order
  /// of first appearance. Returns an empty list for null, empty, or placeholder-free input.
  /// </summary>
  /// <param name="template">String that may contain placeholders.</param>
  public static IReadOnlyList<string> ExtractKeys(string? template) {
    if (string.IsNullOrEmpty(template)) return Array.Empty<string>();
    var keys = new List<string>();
    foreach (Match match in PlaceholderPattern.Matches(template)) {
      var key = match.Groups[1].Value;
      if (!keys.Contains(key, StringComparer.Ordinal)) keys.Add(key);
    }

    return keys;
  }

  /// <summary>True when <paramref name="template"/> contains at least one <c>{{key}}</c> placeholder.</summary>
  /// <param name="template">String to test; null and empty are false.</param>
  public static bool ContainsPlaceholder(string? template) =>
      !string.IsNullOrEmpty(template) && PlaceholderPattern.IsMatch(template);

  /// <summary>
  /// Strict counterpart to <see cref="Substitute"/>: replaces every <c>{{key}}</c> that
  /// <paramref name="context"/> can supply and reports the rest instead of leaving them in place.
  /// </summary>
  /// <param name="template">String that may contain placeholders.</param>
  /// <param name="context">Substitution map from placeholder key to replacement value.</param>
  /// <param name="result">The substituted string. Unresolved keys remain as-is so callers may log it.</param>
  /// <param name="unresolvedKeys">Distinct keys the context could not supply, in order of appearance.</param>
  /// <returns><c>true</c> when every key resolved; otherwise <c>false</c>.</returns>
  public static bool TrySubstitute(
      string? template,
      IReadOnlyDictionary<string, string> context,
      out string result,
      out IReadOnlyList<string> unresolvedKeys) {
    ArgumentNullException.ThrowIfNull(context);
    if (string.IsNullOrEmpty(template)) {
      result = template ?? string.Empty;
      unresolvedKeys = Array.Empty<string>();
      return true;
    }

    var missing = new List<string>();
    result = PlaceholderPattern.Replace(template, m => {
      var key = m.Groups[1].Value;
      if (context.TryGetValue(key, out var value)) return value;
      if (!missing.Contains(key, StringComparer.Ordinal)) missing.Add(key);
      return m.Value;
    });

    unresolvedKeys = missing;
    return missing.Count == 0;
  }

  /// <summary>
  /// Returns a copy of <paramref name="template"/> with every <c>{{key}}</c> token replaced
  /// by the corresponding value from <paramref name="context"/>.
  /// Tokens whose key is absent from <paramref name="context"/> are left as-is.
  /// </summary>
  /// <param name="template">The string that may contain <c>{{key}}</c> placeholders.</param>
  /// <param name="context">Substitution map from placeholder key to replacement value.</param>
  public static string Substitute(string template, IReadOnlyDictionary<string, string> context) {
    return PlaceholderPattern.Replace(template, m => {
      var key = m.Groups[1].Value;
      return context.TryGetValue(key, out var value) ? value : m.Value;
    });
  }

  /// <summary>
  /// Returns a new <see cref="SequenceActionPayload"/> where every textual parameter value has had
  /// its <c>{{key}}</c> placeholders substituted from <paramref name="context"/>. Values that are not
  /// text are copied unchanged.
  /// </summary>
  /// <param name="payload">The payload whose textual parameters should be substituted.</param>
  /// <param name="context">Substitution map from placeholder key to replacement value.</param>
  public static SequenceActionPayload SubstitutePayload(
      SequenceActionPayload payload,
      IReadOnlyDictionary<string, string> context) {
    ArgumentNullException.ThrowIfNull(payload);
    var result = new SequenceActionPayload { Type = payload.Type, SchemaVersion = payload.SchemaVersion };
    foreach (var (key, value) in payload.Parameters) {
      result.Parameters[key] = SubstituteValue(value, context);
    }

    return result;
  }

  /// <summary>
  /// Substitutes one payload slot.
  /// <para>
  /// Handling <see cref="JsonElement"/> is not a nicety: <see cref="SequenceActionPayload.Parameters"/>
  /// is a <c>Dictionary&lt;string, object?&gt;</c>, so a payload loaded from disk holds
  /// <see cref="JsonElement"/> values and <em>never</em> <see cref="string"/>. Matching only on
  /// <see cref="string"/> meant every stored placeholder was copied through untouched — a tap with
  /// <c>"y": "{{sectionRowY}}"</c> reached the device layer as that literal text and failed with
  /// "requires numeric 'x' and 'y'". Only payloads built in memory (that is, in tests) ever
  /// substituted, which is precisely why this survived a green suite.
  /// </para>
  /// <para>
  /// The substituted result is returned as a plain <see cref="string"/>; every consumer parses numeric
  /// slots defensively from text, so a resolved <c>"569"</c> reads back as the number 569.
  /// </para>
  /// <para>
  /// Objects and arrays are copied unchanged — placeholders nested inside a structured value (an OCR
  /// region, say) are not substituted, matching the feature's "string and numeric leaf fields" scope.
  /// </para>
  /// </summary>
  /// <param name="value">The stored slot value.</param>
  /// <param name="context">Substitution map from placeholder key to replacement value.</param>
  private static object? SubstituteValue(object? value, IReadOnlyDictionary<string, string> context) {
    switch (value) {
      case string str:
        return Substitute(str, context);
      case JsonElement { ValueKind: JsonValueKind.String } element: {
        var text = element.GetString();
        return text is null ? value : Substitute(text, context);
      }
      default:
        return value;
    }
  }
}
