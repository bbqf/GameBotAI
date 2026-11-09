namespace GameBot.Domain.Parameters;

/// <summary>
/// The rules of the step-result placeholder (feature 181): <c>{{step.&lt;stepRef&gt;.matchedImageId}}</c>.
/// The prefix <c>step.</c> is a reserved namespace, in the same way as <c>queue.</c>. The value is the id
/// of the image that matched in an image check of the named earlier step.
/// </summary>
public static class StepResultPlaceholderRules {
  /// <summary>The reserved namespace prefix, with the dot.</summary>
  public const string Prefix = "step.";

  /// <summary>The member that gives the matched image id.</summary>
  public const string MatchedImageIdMember = "matchedImageId";

  private const string Suffix = "." + MatchedImageIdMember;

  /// <summary>True when <paramref name="name"/> uses the reserved <c>step.</c> namespace.</summary>
  /// <param name="name">Candidate placeholder name.</param>
  public static bool IsStepResultName(string? name) =>
      name is not null && name.StartsWith(Prefix, System.StringComparison.Ordinal);

  /// <summary>
  /// Reads <c>step.&lt;stepRef&gt;.matchedImageId</c>. Returns false when the name has another form.
  /// </summary>
  /// <param name="name">Candidate placeholder name.</param>
  /// <param name="stepRef">The step name, when the result is true.</param>
  public static bool TryParse(string? name, out string stepRef) {
    stepRef = string.Empty;
    if (name is null
        || !name.StartsWith(Prefix, System.StringComparison.Ordinal)
        || !name.EndsWith(Suffix, System.StringComparison.Ordinal)
        || name.Length <= Prefix.Length + Suffix.Length) {
      return false;
    }

    stepRef = name.Substring(Prefix.Length, name.Length - Prefix.Length - Suffix.Length);
    return true;
  }

  /// <summary>Builds the placeholder name for a step.</summary>
  /// <param name="stepRef">The step name.</param>
  public static string NameFor(string stepRef) => Prefix + stepRef + Suffix;
}
