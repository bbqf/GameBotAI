using System.Collections.Generic;
using System.Globalization;
using GameBot.Domain.Commands;
using GameBot.Domain.Utils;

namespace GameBot.Domain.Parameters;

/// <summary>
/// Applies a <see cref="ParameterScope"/> to a condition tree immediately before the evaluation
/// (feature 114), so that an <c>imageVisible</c> leaf can find the image that a parameter selects.
/// <para>
/// The resolver substitutes each <see cref="ImageVisibleStepCondition.ImageId"/>. The inline form
/// accepts text around the placeholder, for example <c>nova-{{option}}</c>, the same as the inline
/// image field of a command step. Other leaves do not change. The stored condition does not change:
/// the result is a new tree when a leaf has a placeholder, and the same instance when no leaf has one.
/// </para>
/// <para>
/// An unknown name or an empty result gives a <see cref="ParameterResolutionError"/> with the reason
/// <see cref="ParameterResolutionReasons.Unresolved"/>. The caller then fails the step, so a literal
/// <c>{{placeholder}}</c> never goes to the image evaluator.
/// </para>
/// </summary>
public static class SequenceStepConditionResolver {
  /// <summary>
  /// Resolves each <c>imageVisible.imageId</c> in <paramref name="condition"/> against <paramref name="scope"/>.
  /// </summary>
  /// <param name="condition">The stored condition. The method does not change it.</param>
  /// <param name="scope">The scope of the call site.</param>
  /// <param name="fieldPathPrefix">
  /// The field path of the condition, for example <c>condition</c>, <c>if.condition</c>,
  /// <c>loop.condition</c> or <c>breakCondition</c>. The error field path is this prefix, then
  /// <c>.children[i]</c> for each composite level, then <c>.imageId</c>.
  /// </param>
  /// <param name="resolved">The resolved condition. It is the same instance when no leaf has a placeholder.</param>
  /// <param name="error">The first failure, when the resolution did not succeed.</param>
  /// <param name="used">The values that the resolution used. Empty when no leaf has a placeholder.</param>
  /// <returns><c>true</c> when each placeholder resolved; otherwise <c>false</c>.</returns>
  public static bool TryResolve(
      SequenceStepCondition condition,
      ParameterScope scope,
      string fieldPathPrefix,
      out SequenceStepCondition resolved,
      out ParameterResolutionError? error,
      out IReadOnlyList<ResolvedParameter> used) {
    ArgumentNullException.ThrowIfNull(condition);
    ArgumentNullException.ThrowIfNull(scope);
    ArgumentNullException.ThrowIfNull(fieldPathPrefix);

    var usedList = new List<ResolvedParameter>();
    used = usedList;
    error = null;
    resolved = condition;

    // Fast path: no leaf has a placeholder, so the stored condition is the effective condition.
    if (!HasPlaceholder(condition)) return true;

    var context = scope.ToSubstitutionContext();
    var ok = TryResolveNode(condition, scope, context, fieldPathPrefix, usedList, ref error, out var result);
    resolved = ok ? result : condition;
    return ok;
  }

  private static bool HasPlaceholder(SequenceStepCondition? condition) => condition switch {
    ImageVisibleStepCondition image => TemplateSubstitutor.ContainsPlaceholder(image.ImageId),
    CompositeStepCondition composite => composite.Children is not null && AnyChildHasPlaceholder(composite.Children),
    _ => false
  };

  private static bool AnyChildHasPlaceholder(IReadOnlyList<SequenceStepCondition> children) {
    foreach (var child in children) {
      if (HasPlaceholder(child)) return true;
    }

    return false;
  }

  private static bool TryResolveNode(
      SequenceStepCondition condition,
      ParameterScope scope,
      IReadOnlyDictionary<string, string> context,
      string fieldPath,
      List<ResolvedParameter> used,
      ref ParameterResolutionError? error,
      out SequenceStepCondition resolved) {
    resolved = condition;
    switch (condition) {
      case ImageVisibleStepCondition image:
        return TryResolveImage(image, scope, context, $"{fieldPath}.imageId", used, ref error, out resolved);

      case CompositeStepCondition composite when HasPlaceholder(composite):
        var children = new List<SequenceStepCondition>(composite.Children.Count);
        for (var index = 0; index < composite.Children.Count; index++) {
          var childPath = string.Format(CultureInfo.InvariantCulture, "{0}.children[{1}]", fieldPath, index);
          if (!TryResolveNode(composite.Children[index], scope, context, childPath, used, ref error, out var child)) {
            return false;
          }

          children.Add(child);
        }

        resolved = CopyComposite(composite, children);
        return true;

      default:
        return true;
    }
  }

  private static bool TryResolveImage(
      ImageVisibleStepCondition image,
      ParameterScope scope,
      IReadOnlyDictionary<string, string> context,
      string fieldPath,
      List<ResolvedParameter> used,
      ref ParameterResolutionError? error,
      out SequenceStepCondition resolved) {
    resolved = image;
    if (!TemplateSubstitutor.ContainsPlaceholder(image.ImageId)) return true;

    var keys = TemplateSubstitutor.ExtractKeys(image.ImageId);
    if (!TemplateSubstitutor.TrySubstitute(image.ImageId, context, out var substituted, out var unresolved)) {
      error = new ParameterResolutionError(unresolved[0], fieldPath, ParameterResolutionReasons.Unresolved);
      return false;
    }

    // An empty id cannot name an image. This is the same rule as the empty-id check of a command step.
    if (string.IsNullOrWhiteSpace(substituted)) {
      error = new ParameterResolutionError(keys[0], fieldPath, ParameterResolutionReasons.Unresolved);
      return false;
    }

    foreach (var key in keys) {
      if (scope.TryResolve(key, out var value)) used.Add(new ResolvedParameter(key, value.Text, value.OriginLayer));
    }

    resolved = new ImageVisibleStepCondition {
      ImageId = substituted,
      MinSimilarity = image.MinSimilarity,
      Negate = image.Negate
    };
    return true;
  }

  private static CompositeStepCondition CopyComposite(
      CompositeStepCondition composite,
      IReadOnlyList<SequenceStepCondition> children) => composite.Rule switch {
        CompositeConditionRule.All => new AllStepCondition { Children = children, Negate = composite.Negate },
        CompositeConditionRule.Any => new AnyStepCondition { Children = children, Negate = composite.Negate },
        _ => new NoneStepCondition { Children = children, Negate = composite.Negate }
      };
}
