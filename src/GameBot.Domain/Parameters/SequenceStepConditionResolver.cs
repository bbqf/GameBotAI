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
/// image field of a command step. Feature 165 adds <see cref="OcrTextStepCondition.Pattern"/> and
/// <see cref="OcrTextStepCondition.Expected"/> with the same rule. A resolved pattern that is not a valid
/// regular expression gives the reason <see cref="ParameterResolutionReasons.InvalidPattern"/>.
/// Other leaves do not change. The stored condition does not change:
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
    OcrTextStepCondition ocr => TemplateSubstitutor.ContainsPlaceholder(ocr.Pattern)
        || TemplateSubstitutor.ContainsPlaceholder(ocr.Expected),
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

      case OcrTextStepCondition ocr when HasPlaceholder(ocr):
        return TryResolveOcrText(ocr, scope, context, fieldPath, used, ref error, out resolved);

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

    // An empty id cannot name an image. This is the same rule as the empty-id check of a command step.
    if (!TryResolveText(image.ImageId, fieldPath, scope, context, used, ref error, out var substituted)) {
      return false;
    }

    resolved = new ImageVisibleStepCondition {
      ImageId = substituted,
      MinSimilarity = image.MinSimilarity,
      Negate = image.Negate,
      Region = image.Region
    };
    return true;
  }

  /// <summary>
  /// Resolves the <c>pattern</c> and <c>expected</c> fields of an <c>ocrText</c> leaf (feature 165).
  /// The value goes into the field with no regex escape. A resolved pattern that is not a valid regular
  /// expression gives the reason <see cref="ParameterResolutionReasons.InvalidPattern"/>. The method
  /// makes a new leaf. It does not change the input leaf.
  /// </summary>
  private static bool TryResolveOcrText(
      OcrTextStepCondition ocr,
      ParameterScope scope,
      IReadOnlyDictionary<string, string> context,
      string fieldPath,
      List<ResolvedParameter> used,
      ref ParameterResolutionError? error,
      out SequenceStepCondition resolved) {
    resolved = ocr;
    var pattern = ocr.Pattern;
    if (TemplateSubstitutor.ContainsPlaceholder(pattern)) {
      var patternPath = $"{fieldPath}.pattern";
      if (!TryResolveText(pattern!, patternPath, scope, context, used, ref error, out var resolvedPattern)) return false;

      if (!OcrTextConditionRules.TryValidatePattern(resolvedPattern, out _)) {
        var firstKey = TemplateSubstitutor.ExtractKeys(pattern)[0];
        error = new ParameterResolutionError(firstKey, patternPath, ParameterResolutionReasons.InvalidPattern, resolvedPattern);
        return false;
      }

      pattern = resolvedPattern;
    }

    var expected = ocr.Expected;
    if (TemplateSubstitutor.ContainsPlaceholder(expected)
        && !TryResolveText(expected!, $"{fieldPath}.expected", scope, context, used, ref error, out expected)) {
      return false;
    }

    resolved = new OcrTextStepCondition {
      Region = ocr.Region,
      Expected = expected,
      Pattern = pattern,
      Parser = ocr.Parser,
      Negate = ocr.Negate
    };
    return true;
  }

  /// <summary>
  /// Substitutes the placeholders of one text field. An unknown name, or a full result that is empty or
  /// white space, gives the reason <see cref="ParameterResolutionReasons.Unresolved"/>. The method adds
  /// each used value to <paramref name="used"/>.
  /// </summary>
  private static bool TryResolveText(
      string text,
      string fieldPath,
      ParameterScope scope,
      IReadOnlyDictionary<string, string> context,
      List<ResolvedParameter> used,
      ref ParameterResolutionError? error,
      out string substituted) {
    var keys = TemplateSubstitutor.ExtractKeys(text);
    if (!TemplateSubstitutor.TrySubstitute(text, context, out substituted, out var unresolved)) {
      error = new ParameterResolutionError(unresolved[0], fieldPath, ParameterResolutionReasons.Unresolved);
      return false;
    }

    if (string.IsNullOrWhiteSpace(substituted)) {
      error = new ParameterResolutionError(keys[0], fieldPath, ParameterResolutionReasons.Unresolved);
      return false;
    }

    foreach (var key in keys) {
      if (scope.TryResolve(key, out var value)) used.Add(new ResolvedParameter(key, value.Text, value.OriginLayer));
    }

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
