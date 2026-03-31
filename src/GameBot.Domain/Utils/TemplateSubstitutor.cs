using System.Collections.Generic;
using System.Text.RegularExpressions;
using GameBot.Domain.Commands;

namespace GameBot.Domain.Utils;

/// <summary>
/// Substitutes <c>{{key}}</c> template placeholders in strings and action payload parameters.
/// Keys must consist of word characters only (<c>\w+</c>).  Unknown keys are left unchanged.
/// </summary>
public static class TemplateSubstitutor
{
    private static readonly Regex PlaceholderPattern =
        new(@"\{\{(\w+)\}\}", RegexOptions.Compiled, TimeSpan.FromMilliseconds(500));

    /// <summary>
    /// Returns a copy of <paramref name="template"/> with every <c>{{key}}</c> token replaced
    /// by the corresponding value from <paramref name="context"/>.
    /// Tokens whose key is absent from <paramref name="context"/> are left as-is.
    /// </summary>
    /// <param name="template">The string that may contain <c>{{key}}</c> placeholders.</param>
    /// <param name="context">Substitution map from placeholder key to replacement value.</param>
    public static string Substitute(string template, IReadOnlyDictionary<string, string> context)
    {
        return PlaceholderPattern.Replace(template, m =>
        {
            var key = m.Groups[1].Value;
            return context.TryGetValue(key, out var value) ? value : m.Value;
        });
    }

    /// <summary>
    /// Returns a new <see cref="SequenceActionPayload"/> where every <em>string</em> parameter
    /// value has had its <c>{{key}}</c> placeholders substituted from <paramref name="context"/>.
    /// Non-string values are copied unchanged.
    /// </summary>
    /// <param name="payload">The payload whose string parameters should be substituted.</param>
    /// <param name="context">Substitution map from placeholder key to replacement value.</param>
    public static SequenceActionPayload SubstitutePayload(
        SequenceActionPayload payload,
        IReadOnlyDictionary<string, string> context)
    {
        ArgumentNullException.ThrowIfNull(payload);
        var result = new SequenceActionPayload { Type = payload.Type };
        foreach (var (key, value) in payload.Parameters)
        {
            result.Parameters[key] = value is string str ? Substitute(str, context) : value;
        }

        return result;
    }
}
