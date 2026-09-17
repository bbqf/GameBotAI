using System.Collections.Generic;
using System.Globalization;
using GameBot.Domain.Commands;

namespace GameBot.Domain.Services;

/// <summary>
/// Save-time validation for composite step conditions (feature 088, issue #191).
/// <para>
/// This runs on the sequence save path, which turns a rejection into a 400 with an actionable
/// message. It matters that it runs there and not only deeper down: <c>FileSequenceRepository</c>
/// throws on a malformed condition, and anything that reaches it unvalidated surfaces as a 500.
/// </para>
/// <para>
/// Paths in messages are <c>$</c>-rooted at the condition being validated
/// (<c>$.children[2].children[0]</c>), matching the convention
/// <see cref="ConditionExpression.Validate"/> already uses for the block-style flow conditions, so
/// an error from either validator reads the same way.
/// </para>
/// </summary>
public static class CompositeConditionValidator {
  /// <summary>
  /// The set of <c>commandOutcome</c> expected states a condition may name. Mirrors the list the
  /// per-step validator enforces for a top-level condition.
  /// </summary>
  private static readonly HashSet<string> AllowedCommandOutcomeStates =
    new(System.StringComparer.OrdinalIgnoreCase) { "success", "failed", "skipped", "break", "no_break" };

  /// <summary>
  /// Validates <paramref name="condition"/> and everything beneath it, appending one message per
  /// problem to <paramref name="errors"/>.
  /// </summary>
  /// <param name="condition">The condition to validate. A leaf is checked for shape; a composite is
  /// checked for shape, size and depth, then walked.</param>
  /// <param name="stepLabel">Step identifier used to prefix every message, so an author can tell
  /// which step in a large sequence is at fault.</param>
  /// <param name="errors">Collector the caller already uses for this step's other errors.</param>
  /// <remarks>
  /// Leaf conditions reached through a composite are validated here; a leaf sitting directly in a
  /// condition slot keeps being validated by its existing caller, so this method does not duplicate
  /// those messages. See <paramref name="validateLeafAtRoot"/>.
  /// </remarks>
  /// <param name="validateLeafAtRoot">
  /// When <c>false</c> (the default) a leaf passed directly as <paramref name="condition"/> is left
  /// alone, because the caller already checks it and would otherwise report the same problem twice.
  /// Children of a composite are always validated regardless.
  /// </param>
  /// <param name="positionByStepId">
  /// Every step id in the sequence mapped to its position in the authored (document) order walk.
  /// Supply it to have a nested <c>commandOutcome</c>'s <c>stepRef</c> resolved, exactly as the
  /// per-step validator resolves one written directly (feature 103, issue #193 — before that, a
  /// reference reached through a composite was checked for non-emptiness and nothing else, so a
  /// dangling one saved successfully and failed the run instead).
  /// <para>
  /// Optional because this validator is called from sites that have no such index — notably the
  /// repository's own composite walk, where a rejection surfaces as a 500 rather than a 400. Those
  /// callers pass nothing and keep the shape-only behaviour they have always had.
  /// </para>
  /// </param>
  /// <param name="referencingStepPosition">
  /// The position, in that same walk, of the step carrying this condition. Supply it together with
  /// <paramref name="positionByStepId"/> to also enforce that a reference names a <em>prior</em>
  /// step. Resolution and ordering are separate rules: given the index alone, only resolution is
  /// checked.
  /// </param>
  public static void Validate(
      SequenceStepCondition? condition,
      string stepLabel,
      ICollection<string> errors,
      bool validateLeafAtRoot = false,
      IReadOnlyDictionary<string, int>? positionByStepId = null,
      int? referencingStepPosition = null) {
    if (condition is null) {
      return;
    }

    System.ArgumentNullException.ThrowIfNull(errors);

    if (condition is not CompositeStepCondition && !validateLeafAtRoot) {
      return;
    }

    Walk(condition, stepLabel, "$", depth: 1, errors, positionByStepId, referencingStepPosition);
  }

  private static void Walk(
      SequenceStepCondition condition,
      string stepLabel,
      string path,
      int depth,
      ICollection<string> errors,
      IReadOnlyDictionary<string, int>? positionByStepId,
      int? referencingStepPosition) {
    if (depth > CompositeStepCondition.MaxDepth) {
      errors.Add($"Step '{stepLabel}' condition at {path}: condition nesting exceeds the maximum depth of {CompositeStepCondition.MaxDepth}.");
      return;
    }

    switch (condition) {
      case CompositeStepCondition composite:
        ValidateComposite(composite, stepLabel, path, depth, errors, positionByStepId, referencingStepPosition);
        break;

      case ImageVisibleStepCondition imageVisible:
        if (string.IsNullOrWhiteSpace(imageVisible.ImageId)) {
          errors.Add($"Step '{stepLabel}' condition at {path}: imageVisible condition requires imageId.");
        }

        if (imageVisible.MinSimilarity is < 0 or > 1) {
          errors.Add($"Step '{stepLabel}' condition at {path}: imageVisible minSimilarity must be within 0..1.");
        }

        break;

      case CommandOutcomeStepCondition commandOutcome:
        if (string.IsNullOrWhiteSpace(commandOutcome.StepRef)) {
          errors.Add($"Step '{stepLabel}' condition at {path}: commandOutcome condition requires stepRef.");
        }
        else {
          ValidateStepReference(commandOutcome.StepRef, stepLabel, path, errors, positionByStepId, referencingStepPosition);
        }

        if (string.IsNullOrWhiteSpace(commandOutcome.ExpectedState)
            || !AllowedCommandOutcomeStates.Contains(commandOutcome.ExpectedState)) {
          errors.Add($"Step '{stepLabel}' condition at {path}: commandOutcome expectedState must be one of success|failed|skipped|break|no_break.");
        }

        break;

      default:
        errors.Add($"Step '{stepLabel}' condition at {path}: unsupported condition type '{condition.Type}'.");
        break;
    }
  }

  /// <summary>
  /// Applies the two reference rules a nested <c>commandOutcome</c> is subject to, when the caller
  /// supplied what they need (feature 103, FR-008/FR-009). Kept as its own method so the
  /// <see cref="Walk"/> switch stays small: this repository's build-time analyzers degrade on large
  /// methods and <c>TreatWarningsAsErrors</c> turns that into a build failure.
  /// <para>
  /// The messages deliberately reuse the per-step validator's wording, prefixed with this
  /// validator's <c>$</c>-rooted path, so an author reads one format whichever rule caught the
  /// problem and whichever slot the condition sits in.
  /// </para>
  /// </summary>
  private static void ValidateStepReference(
      string stepRef,
      string stepLabel,
      string path,
      ICollection<string> errors,
      IReadOnlyDictionary<string, int>? positionByStepId,
      int? referencingStepPosition) {
    if (positionByStepId is null) {
      return;
    }

    if (!positionByStepId.TryGetValue(stepRef, out var referencedPosition)) {
      errors.Add($"Step '{stepLabel}' condition at {path}: commandOutcome references unknown prior step '{stepRef}'.");
      return;
    }

    // Resolution succeeded; ordering is only checked when the caller said where the referencing step
    // sits, because "prior" is meaningless without it.
    if (referencingStepPosition is { } ownPosition && referencedPosition >= ownPosition) {
      errors.Add($"Step '{stepLabel}' condition at {path}: commandOutcome stepRef '{stepRef}' must reference a prior step.");
    }
  }

  private static void ValidateComposite(
      CompositeStepCondition composite,
      string stepLabel,
      string path,
      int depth,
      ICollection<string> errors,
      IReadOnlyDictionary<string, int>? positionByStepId,
      int? referencingStepPosition) {
    var rule = RuleName(composite.Rule);
    var children = composite.Children;

    if (children is null || children.Count == 0) {
      // Deliberately an error rather than a default truth value: "combine nothing" has no answer a
      // reader would agree on, and picking one silently would make an authoring slip act like a
      // guard that always passes.
      errors.Add($"Step '{stepLabel}' condition at {path}: '{rule}' requires at least one child.");
      return;
    }

    if (children.Count > CompositeStepCondition.MaxChildren) {
      errors.Add(string.Format(
        CultureInfo.InvariantCulture,
        "Step '{0}' condition at {1}: '{2}' allows at most {3} children (found {4}).",
        stepLabel,
        path,
        rule,
        CompositeStepCondition.MaxChildren,
        children.Count));
      return;
    }

    for (var index = 0; index < children.Count; index++) {
      var child = children[index];
      var childPath = string.Format(CultureInfo.InvariantCulture, "{0}.children[{1}]", path, index);

      if (child is null) {
        errors.Add($"Step '{stepLabel}' condition at {childPath}: child condition is missing.");
        continue;
      }

      Walk(child, stepLabel, childPath, depth + 1, errors, positionByStepId, referencingStepPosition);
    }
  }

  /// <summary>
  /// Renders a rule as the discriminator an author wrote, for use in messages. Every enum member is
  /// mapped, so the fallback is only reachable if a rule is added without updating this method.
  /// </summary>
  internal static string RuleName(CompositeConditionRule rule) => rule switch {
    CompositeConditionRule.All => "all",
    CompositeConditionRule.Any => "any",
    CompositeConditionRule.None => "none",
    _ => rule.ToString()
  };
}
