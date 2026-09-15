using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using GameBot.Domain.Commands;
using GameBot.Domain.Commands.Blocks;

namespace GameBot.Domain.Services;

/// <summary>Why a condition could not be evaluated at all, as distinct from evaluating to false.</summary>
public enum ConditionEvaluationFailureKind {
  /// <summary>No image evaluator was supplied, so an <c>imageVisible</c> leaf cannot be answered.</summary>
  ImageEvaluatorUnavailable,

  /// <summary>A <c>commandOutcome</c> leaf names a step whose outcome is not available.</summary>
  CommandOutcomeUnavailable,

  /// <summary>The condition is of a kind this evaluator does not know how to answer.</summary>
  UnsupportedCondition
}

/// <summary>
/// Raised when a condition cannot be evaluated. Deliberately distinct from a condition that
/// evaluates to <c>false</c>: a guard that could not be answered must not be treated as "the screen
/// did not match", because that silently turns a broken setup into a skipped step.
/// </summary>
public sealed class ConditionEvaluationException : InvalidOperationException {
  /// <summary>What went wrong, so a caller can render its own message.</summary>
  public ConditionEvaluationFailureKind Kind { get; }

  /// <summary>The relevant identifier, such as the unresolvable step reference. May be empty.</summary>
  public string Detail { get; }

  /// <summary>Creates a failure of the given kind.</summary>
  public ConditionEvaluationException(ConditionEvaluationFailureKind kind, string detail, string message)
    : base(message) {
    Kind = kind;
    Detail = detail ?? string.Empty;
  }

  /// <summary>Creates a failure with no specific kind. Present to satisfy the standard exception shape.</summary>
  public ConditionEvaluationException() : this(ConditionEvaluationFailureKind.UnsupportedCondition, string.Empty, "Condition evaluation failed.") { }

  /// <summary>Creates a failure with a message. Present to satisfy the standard exception shape.</summary>
  public ConditionEvaluationException(string message) : this(ConditionEvaluationFailureKind.UnsupportedCondition, string.Empty, message) { }

  /// <summary>Creates a failure wrapping an inner exception.</summary>
  public ConditionEvaluationException(string message, Exception innerException) : base(message, innerException) {
    Kind = ConditionEvaluationFailureKind.UnsupportedCondition;
    Detail = string.Empty;
  }
}

/// <summary>
/// The outcome of evaluating one condition tree.
/// </summary>
/// <param name="Value">The condition's result, with any <c>negate</c> already applied.</param>
/// <param name="DecidingPath">
/// For a composite, the <c>$</c>-rooted path of the child that settled the result — the first false
/// child of an <c>all</c>, the first true child of an <c>any</c> or <c>none</c>. Null when no single
/// child settled it (every child agreed) or when the condition was a leaf.
/// </param>
/// <param name="DecidingDescription">A short rendering of that child, for the execution log.</param>
public readonly record struct ConditionEvaluation(bool Value, string? DecidingPath, string? DecidingDescription);

/// <summary>
/// Evaluates a <see cref="SequenceStepCondition"/>, including composites (feature 088, issue #191).
/// <para>
/// This lives in its own class rather than inside <c>SequenceRunner</c> for three reasons: the runner
/// evaluates conditions in two separate places and the semantics must not drift between them; the
/// runner is already very large and this repository's build-time analyzers degrade badly on large
/// methods, with <c>TreatWarningsAsErrors</c> turning that into a build failure; and a standalone
/// evaluator is unit-testable over a truth table with no sequence, session or emulator.
/// </para>
/// <para>
/// The evaluator performs no screen capture of its own — it asks the supplied image evaluator, which
/// reads through the screen source's capture cache. Children are evaluated back to back with nothing
/// in between, so a composite's children are judged against one screen observation and a two-image
/// guard costs no more capture than a one-image guard.
/// </para>
/// </summary>
public static class SequenceStepConditionEvaluator {
  /// <summary>
  /// Evaluates <paramref name="condition"/>, recursing through composites.
  /// </summary>
  /// <param name="condition">The condition to evaluate.</param>
  /// <param name="imageEvaluator">
  /// Resolves an <c>imageVisible</c> leaf against the current screen. May be null, in which case
  /// reaching such a leaf raises <see cref="ConditionEvaluationException"/> rather than answering false.
  /// </param>
  /// <param name="stepOutcomes">Outcomes recorded by earlier steps, keyed by step id.</param>
  /// <param name="ct">Cancellation token.</param>
  /// <returns>The result plus, for a composite, which child settled it.</returns>
  /// <exception cref="ConditionEvaluationException">The condition could not be evaluated.</exception>
  public static Task<ConditionEvaluation> EvaluateAsync(
      SequenceStepCondition condition,
      Func<Condition, CancellationToken, Task<bool>>? imageEvaluator,
      IReadOnlyDictionary<string, string> stepOutcomes,
      CancellationToken ct = default) {
    ArgumentNullException.ThrowIfNull(condition);
    ArgumentNullException.ThrowIfNull(stepOutcomes);

    return EvaluateNodeAsync(condition, "$", imageEvaluator, stepOutcomes, ct);
  }

  private static async Task<ConditionEvaluation> EvaluateNodeAsync(
      SequenceStepCondition condition,
      string path,
      Func<Condition, CancellationToken, Task<bool>>? imageEvaluator,
      IReadOnlyDictionary<string, string> stepOutcomes,
      CancellationToken ct) {
    ct.ThrowIfCancellationRequested();

    if (condition is CompositeStepCondition composite) {
      return await EvaluateCompositeAsync(composite, path, imageEvaluator, stepOutcomes, ct).ConfigureAwait(false);
    }

    var leafResult = await EvaluateLeafAsync(condition, imageEvaluator, stepOutcomes, ct).ConfigureAwait(false);
    return new ConditionEvaluation(condition.Negate ? !leafResult : leafResult, null, null);
  }

  private static async Task<bool> EvaluateLeafAsync(
      SequenceStepCondition condition,
      Func<Condition, CancellationToken, Task<bool>>? imageEvaluator,
      IReadOnlyDictionary<string, string> stepOutcomes,
      CancellationToken ct) {
    switch (condition) {
      case ImageVisibleStepCondition image:
        if (imageEvaluator is null) {
          throw new ConditionEvaluationException(
            ConditionEvaluationFailureKind.ImageEvaluatorUnavailable,
            image.ImageId ?? string.Empty,
            "Image-visible condition evaluator is unavailable.");
        }

        return await imageEvaluator(new Condition {
          Source = "image",
          TargetId = image.ImageId,
          Mode = "Present",
          ConfidenceThreshold = image.MinSimilarity
        }, ct).ConfigureAwait(false);

      case CommandOutcomeStepCondition outcome:
        if (string.IsNullOrWhiteSpace(outcome.StepRef)
            || !stepOutcomes.TryGetValue(outcome.StepRef, out var actual)) {
          throw new ConditionEvaluationException(
            ConditionEvaluationFailureKind.CommandOutcomeUnavailable,
            outcome.StepRef ?? string.Empty,
            $"commandOutcome reference '{outcome.StepRef}' is not available.");
        }

        return string.Equals(actual, outcome.ExpectedState, StringComparison.OrdinalIgnoreCase);

      default:
        throw new ConditionEvaluationException(
          ConditionEvaluationFailureKind.UnsupportedCondition,
          condition.Type,
          $"Unsupported condition type '{condition.Type}'.");
    }
  }

  private static async Task<ConditionEvaluation> EvaluateCompositeAsync(
      CompositeStepCondition composite,
      string path,
      Func<Condition, CancellationToken, Task<bool>>? imageEvaluator,
      IReadOnlyDictionary<string, string> stepOutcomes,
      CancellationToken ct) {
    // An empty child list is rejected at save time; reaching one here means a caller bypassed
    // validation, so fail loudly rather than inventing a truth value.
    var children = composite.Children;
    if (children is null || children.Count == 0) {
      throw new ConditionEvaluationException(
        ConditionEvaluationFailureKind.UnsupportedCondition,
        CompositeConditionValidator.RuleName(composite.Rule),
        $"Composite condition '{CompositeConditionValidator.RuleName(composite.Rule)}' has no children.");
    }

    // The value a child must take to settle the result on its own, and the result it then produces.
    // Expressed once so the three rules cannot drift apart, and so short-circuiting is the same walk
    // in every case rather than three hand-written loops.
    var (settlingChildValue, settledResult) = composite.Rule switch {
      CompositeConditionRule.All => (false, false),
      CompositeConditionRule.Any => (true, true),
      CompositeConditionRule.None => (true, false),
      _ => throw new ConditionEvaluationException(
        ConditionEvaluationFailureKind.UnsupportedCondition,
        composite.Rule.ToString(),
        $"Unsupported composite rule '{composite.Rule}'.")
    };

    for (var index = 0; index < children.Count; index++) {
      var childPath = string.Format(CultureInfo.InvariantCulture, "{0}.children[{1}]", path, index);
      var child = children[index];
      var childResult = await EvaluateNodeAsync(child, childPath, imageEvaluator, stepOutcomes, ct).ConfigureAwait(false);

      if (childResult.Value != settlingChildValue) {
        continue;
      }

      // Short-circuit: the outcome is settled, so no later child is evaluated. That is what keeps a
      // two-signal guard as cheap as a one-signal guard, and it also means a later child that could
      // not be evaluated is never reached.
      return new ConditionEvaluation(
        composite.Negate ? !settledResult : settledResult,
        childPath,
        Describe(child));
    }

    // No child settled it, so the result is the opposite of the settled one: every child of an "all"
    // was true, or no child of an "any"/"none" was.
    var combined = !settledResult;
    return new ConditionEvaluation(composite.Negate ? !combined : combined, null, null);
  }

  /// <summary>
  /// Renders a condition compactly for an execution-log message, so a reader can see which signal
  /// settled a guard without opening the sequence.
  /// </summary>
  public static string Describe(SequenceStepCondition condition) {
    ArgumentNullException.ThrowIfNull(condition);

    var negatePrefix = condition.Negate ? "NOT " : string.Empty;

    switch (condition) {
      case ImageVisibleStepCondition image:
        var similarity = image.MinSimilarity?.ToString(CultureInfo.InvariantCulture) ?? "default";
        return $"{negatePrefix}imageVisible(imageId={image.ImageId}, minSimilarity={similarity})";

      case CommandOutcomeStepCondition outcome:
        return $"{negatePrefix}commandOutcome(stepRef={outcome.StepRef}, expected={outcome.ExpectedState})";

      case CompositeStepCondition composite:
        var rendered = new List<string>(composite.Children?.Count ?? 0);
        foreach (var child in composite.Children ?? Array.Empty<SequenceStepCondition>()) {
          rendered.Add(Describe(child));
        }

        return $"{negatePrefix}{CompositeConditionValidator.RuleName(composite.Rule)}({string.Join(", ", rendered)})";

      default:
        return $"{negatePrefix}{condition.Type}";
    }
  }
}
