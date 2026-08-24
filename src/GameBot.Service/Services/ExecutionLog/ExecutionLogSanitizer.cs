using System.Text.RegularExpressions;
using GameBot.Domain.Logging;

namespace GameBot.Service.Services.ExecutionLog;

internal static class ExecutionLogSanitizer {
  private static readonly Regex SecretKeyRegex = new("(token|password|secret|apikey|api_key|authorization)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

  public static IReadOnlyList<ExecutionDetailItem> SanitizeDetails(IReadOnlyList<ExecutionDetailItem> details) {
    if (details.Count == 0) return details;

    var sanitized = new List<ExecutionDetailItem>(details.Count);
    foreach (var detail in details) {
      var attributes = detail.Attributes is null
        ? null
        : detail.Attributes.ToDictionary(
          kvp => kvp.Key,
          kvp => MaskIfSensitive(kvp.Key, kvp.Value));

      var sensitivity = attributes is null || attributes.Count == 0
        ? detail.Sensitivity
        : attributes.Values.Any(v => string.Equals(v as string, "[REDACTED]", StringComparison.Ordinal))
          ? "redacted"
          : detail.Sensitivity;

      sanitized.Add(new ExecutionDetailItem(detail.Kind, detail.Message, attributes, sensitivity));
    }

    return sanitized;
  }

  private static object? MaskIfSensitive(string key, object? value) {
    if (value is null) return null;
    if (!SecretKeyRegex.IsMatch(key)) return value;
    return "[REDACTED]";
  }

  /// <summary>
  /// Masks a resolved parameter's value when the <em>parameter name</em> looks like a secret
  /// (feature 078). Parameter values are otherwise recorded verbatim — they are device serials,
  /// instance names and timings, and diagnosability is the point — but an operator is free to declare
  /// a parameter called <c>password</c>, and the constitution forbids leaking that into a log.
  /// </summary>
  /// <param name="parameterName">The declared parameter name.</param>
  /// <param name="value">The resolved value.</param>
  public static string MaskParameterValue(string parameterName, string value) =>
    SecretKeyRegex.IsMatch(parameterName) ? "[REDACTED]" : value;
}
