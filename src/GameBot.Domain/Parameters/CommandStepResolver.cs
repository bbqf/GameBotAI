using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using GameBot.Domain.Commands;
using GameBot.Domain.Utils;

namespace GameBot.Domain.Parameters;

/// <summary>
/// Applies a <see cref="ParameterScope"/> to a <see cref="CommandStep"/> immediately before dispatch
/// (feature 078), producing a step whose fields hold real values instead of placeholders.
/// <para>
/// Two mechanisms, because the type system allows only one of them: string-typed fields carry their
/// placeholder inline and are substituted in place (and may embed a placeholder in surrounding text),
/// while numeric fields are supplied by the step's <see cref="CommandStep.FieldTemplates"/> overlay
/// and must be a whole-field placeholder so the result can be parsed.
/// </para>
/// <para>
/// Resolution never degrades silently: an unknown name or an unparseable number produces a
/// <see cref="ParameterResolutionError"/> and the caller fails the step, so a literal
/// <c>{{placeholder}}</c> can never reach a device action.
/// </para>
/// </summary>
public static class CommandStepResolver {
  /// <summary>
  /// Resolves every parameter reference in <paramref name="step"/> against <paramref name="scope"/>.
  /// </summary>
  /// <param name="step">The stored step, left unmodified.</param>
  /// <param name="scope">Scope in effect at this point of the invocation chain.</param>
  /// <param name="resolved">A clone with substituted values, when resolution succeeded.</param>
  /// <param name="error">The first failure encountered, when resolution did not succeed.</param>
  /// <param name="resolvedParameters">
  /// The values actually used, keyed by parameter name, for execution-log attribution. Empty when the
  /// step referenced no parameters.
  /// </param>
  /// <returns><c>true</c> when the step resolved fully; otherwise <c>false</c>.</returns>
  public static bool TryResolve(
      CommandStep step,
      ParameterScope scope,
      [NotNullWhen(true)] out CommandStep? resolved,
      out ParameterResolutionError? error,
      out IReadOnlyList<ResolvedParameter> resolvedParameters) {
    ArgumentNullException.ThrowIfNull(step);
    ArgumentNullException.ThrowIfNull(scope);

    resolved = null;
    error = null;
    var used = new List<ResolvedParameter>();
    resolvedParameters = used;

    var hasInlinePlaceholder = HasInlinePlaceholder(step);
    var hasOverlay = step.FieldTemplates is { Count: > 0 };
    if (!hasInlinePlaceholder && !hasOverlay) {
      // Fast path: nothing is parametrized, so the stored step is already the effective step.
      resolved = step;
      return true;
    }

    var context = scope.ToSubstitutionContext();

    string? targetId = step.TargetId;
    if (step.Type != CommandStepType.Command
        && !TryText(step.TargetId, "targetId", context, scope, used, ref error, out targetId)) {
      return false;
    }

    KeyInputConfig? keyInput = step.KeyInput;
    if (step.KeyInput is not null) {
      if (!TryText(step.KeyInput.Key, "keyInput.key", context, scope, used, ref error, out var key)) return false;
      keyInput = new KeyInputConfig { Key = key! };
    }

    EnsureEmulatorRunningConfig? ensureEmulator = step.EnsureEmulatorRunning;
    if (step.EnsureEmulatorRunning is not null) {
      if (!TryText(step.EnsureEmulatorRunning.AdbSerial, "ensureEmulatorRunning.adbSerial",
              context, scope, used, ref error, out var serial)) return false;
      if (!TryText(step.EnsureEmulatorRunning.InstanceName, "ensureEmulatorRunning.instanceName",
              context, scope, used, ref error, out var instanceName)) return false;
      if (!TryInt(step, "ensureEmulatorRunning.instanceIndex", step.EnsureEmulatorRunning.InstanceIndex,
              scope, used, ref error, out var instanceIndex)) return false;
      ensureEmulator = new EnsureEmulatorRunningConfig {
        AdbSerial = serial!,
        InstanceName = instanceName,
        InstanceIndex = instanceIndex
      };
    }

    SwipeConfig? swipe = step.Swipe;
    if (step.Swipe is not null) {
      if (!TryInt(step, "swipe.startX", step.Swipe.StartX, scope, used, ref error, out var startX)) return false;
      if (!TryInt(step, "swipe.startY", step.Swipe.StartY, scope, used, ref error, out var startY)) return false;
      if (!TryInt(step, "swipe.endX", step.Swipe.EndX, scope, used, ref error, out var endX)) return false;
      if (!TryInt(step, "swipe.endY", step.Swipe.EndY, scope, used, ref error, out var endY)) return false;
      if (!TryInt(step, "swipe.durationMs", step.Swipe.DurationMs, scope, used, ref error, out var duration)) return false;
      swipe = new SwipeConfig {
        StartX = startX ?? 0, StartY = startY ?? 0,
        EndX = endX ?? 0, EndY = endY ?? 0,
        DurationMs = duration
      };
    }

    PrimitiveTapConfig? primitiveTap = step.PrimitiveTap;
    if (step.PrimitiveTap is not null) {
      if (!TryDetection(step, step.PrimitiveTap.DetectionTarget, "primitiveTap.detectionTarget",
              context, scope, used, ref error, out var target)) return false;
      primitiveTap = new PrimitiveTapConfig { DetectionTarget = target! };
    }

    WaitForImageConfig? waitForImage = step.WaitForImage;
    if (step.WaitForImage is not null) {
      if (!TryDetection(step, step.WaitForImage.DetectionTarget, "waitForImage.detectionTarget",
              context, scope, used, ref error, out var target)) return false;
      if (!TryInt(step, "waitForImage.timeoutMs", step.WaitForImage.TimeoutMs, scope, used, ref error, out var timeout))
        return false;
      waitForImage = new WaitForImageConfig { DetectionTarget = target, TimeoutMs = timeout ?? step.WaitForImage.TimeoutMs };
    }

    EnsureGameRunningConfig? ensureGame = step.EnsureGameRunning;
    if (step.EnsureGameRunning is not null) {
      if (!TryDetection(step, step.EnsureGameRunning.ReadinessImage, "ensureGameRunning.readinessImage",
              context, scope, used, ref error, out var readiness)) return false;
      if (!TryInt(step, "ensureGameRunning.readinessTimeoutMs", step.EnsureGameRunning.ReadinessTimeoutMs,
              scope, used, ref error, out var readinessTimeout)) return false;
      ensureGame = new EnsureGameRunningConfig {
        ReadinessImage = readiness,
        ReadinessTimeoutMs = readinessTimeout ?? step.EnsureGameRunning.ReadinessTimeoutMs
      };
    }

    resolved = new CommandStep {
      Type = step.Type,
      TargetId = targetId ?? string.Empty,
      PrimitiveTap = primitiveTap,
      WaitForImage = waitForImage,
      KeyInput = keyInput,
      Swipe = swipe,
      EnsureEmulatorRunning = ensureEmulator,
      EnsureGameRunning = ensureGame,
      Order = step.Order,
      FieldTemplates = step.FieldTemplates,
      ParameterBindings = step.ParameterBindings
    };
    return true;
  }

  private static bool HasInlinePlaceholder(CommandStep step) =>
      (step.Type != CommandStepType.Command && TemplateSubstitutor.ContainsPlaceholder(step.TargetId))
      || TemplateSubstitutor.ContainsPlaceholder(step.KeyInput?.Key)
      || TemplateSubstitutor.ContainsPlaceholder(step.EnsureEmulatorRunning?.AdbSerial)
      || TemplateSubstitutor.ContainsPlaceholder(step.EnsureEmulatorRunning?.InstanceName)
      || TemplateSubstitutor.ContainsPlaceholder(step.PrimitiveTap?.DetectionTarget?.ReferenceImageId)
      || TemplateSubstitutor.ContainsPlaceholder(step.WaitForImage?.DetectionTarget?.ReferenceImageId)
      || TemplateSubstitutor.ContainsPlaceholder(step.EnsureGameRunning?.ReadinessImage?.ReferenceImageId);

  private static bool TryText(
      string? value,
      string fieldPath,
      IReadOnlyDictionary<string, string> context,
      ParameterScope scope,
      List<ResolvedParameter> used,
      ref ParameterResolutionError? error,
      out string? resolved) {
    resolved = value;
    if (!TemplateSubstitutor.ContainsPlaceholder(value)) return true;

    if (!TemplateSubstitutor.TrySubstitute(value, context, out var substituted, out var unresolved)) {
      error = new ParameterResolutionError(unresolved[0], fieldPath, ParameterResolutionReasons.Unresolved);
      return false;
    }

    RecordUsage(value!, scope, used);
    resolved = substituted;
    return true;
  }

  private static bool TryDetection(
      CommandStep step,
      DetectionTarget? target,
      string fieldPathPrefix,
      IReadOnlyDictionary<string, string> context,
      ParameterScope scope,
      List<ResolvedParameter> used,
      ref ParameterResolutionError? error,
      out DetectionTarget? resolved) {
    resolved = target;
    if (target is null) return true;

    if (!TryText(target.ReferenceImageId, $"{fieldPathPrefix}.referenceImageId",
            context, scope, used, ref error, out var imageId)) return false;
    if (!TryDouble(step, $"{fieldPathPrefix}.confidence", target.Confidence, scope, used, ref error, out var confidence))
      return false;
    if (!TryInt(step, $"{fieldPathPrefix}.offsetX", target.OffsetX, scope, used, ref error, out var offsetX))
      return false;
    if (!TryInt(step, $"{fieldPathPrefix}.offsetY", target.OffsetY, scope, used, ref error, out var offsetY))
      return false;

    if (string.IsNullOrWhiteSpace(imageId)) {
      error = new ParameterResolutionError(
          "referenceImageId", $"{fieldPathPrefix}.referenceImageId", ParameterResolutionReasons.Unresolved);
      return false;
    }

    var effectiveConfidence = confidence ?? target.Confidence;
    if (effectiveConfidence < 0.0 || effectiveConfidence > 1.0) {
      error = new ParameterResolutionError(
          NameFromTemplate(step, $"{fieldPathPrefix}.confidence"),
          $"{fieldPathPrefix}.confidence",
          ParameterResolutionReasons.NotANumber,
          effectiveConfidence.ToString(CultureInfo.InvariantCulture));
      return false;
    }

    resolved = new DetectionTarget(
        imageId!,
        effectiveConfidence,
        offsetX ?? target.OffsetX,
        offsetY ?? target.OffsetY,
        target.SelectionStrategy);
    return true;
  }

  private static bool TryInt(
      CommandStep step,
      string fieldPath,
      int? current,
      ParameterScope scope,
      List<ResolvedParameter> used,
      ref ParameterResolutionError? error,
      out int? resolved) {
    resolved = current;
    if (!TryOverlayValue(step, fieldPath, scope, used, ref error, out var text)) return false;
    if (text is null) return true;

    if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)) {
      error = new ParameterResolutionError(
          NameFromTemplate(step, fieldPath), fieldPath, ParameterResolutionReasons.NotANumber, text);
      return false;
    }

    resolved = parsed;
    return true;
  }

  private static bool TryDouble(
      CommandStep step,
      string fieldPath,
      double current,
      ParameterScope scope,
      List<ResolvedParameter> used,
      ref ParameterResolutionError? error,
      out double? resolved) {
    resolved = current;
    if (!TryOverlayValue(step, fieldPath, scope, used, ref error, out var text)) return false;
    if (text is null) return true;

    if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)) {
      error = new ParameterResolutionError(
          NameFromTemplate(step, fieldPath), fieldPath, ParameterResolutionReasons.NotANumber, text);
      return false;
    }

    resolved = parsed;
    return true;
  }

  /// <summary>
  /// Resolves one overlay entry. Numeric fields accept only a whole-field placeholder, so the whole
  /// stored template must be a single reference; anything else is reported as a numeric failure.
  /// </summary>
  private static bool TryOverlayValue(
      CommandStep step,
      string fieldPath,
      ParameterScope scope,
      List<ResolvedParameter> used,
      ref ParameterResolutionError? error,
      out string? text) {
    text = null;
    if (step.FieldTemplates is null || !step.FieldTemplates.TryGetValue(fieldPath, out var template)) return true;

    var keys = TemplateSubstitutor.ExtractKeys(template);
    if (keys.Count != 1 || template.Trim() != $"{{{{{keys[0]}}}}}") {
      error = new ParameterResolutionError(
          keys.Count > 0 ? keys[0] : template, fieldPath, ParameterResolutionReasons.NotANumber, template);
      return false;
    }

    if (!scope.TryResolve(keys[0], out var value)) {
      error = new ParameterResolutionError(keys[0], fieldPath, ParameterResolutionReasons.Unresolved);
      return false;
    }

    used.Add(new ResolvedParameter(keys[0], value.Text, value.OriginLayer));
    text = value.Text;
    return true;
  }

  private static string NameFromTemplate(CommandStep step, string fieldPath) {
    if (step.FieldTemplates is not null && step.FieldTemplates.TryGetValue(fieldPath, out var template)) {
      var keys = TemplateSubstitutor.ExtractKeys(template);
      if (keys.Count > 0) return keys[0];
    }

    return fieldPath;
  }

  private static void RecordUsage(string template, ParameterScope scope, List<ResolvedParameter> used) {
    foreach (var key in TemplateSubstitutor.ExtractKeys(template)) {
      if (scope.TryResolve(key, out var value)) {
        used.Add(new ResolvedParameter(key, value.Text, value.OriginLayer));
      }
    }
  }
}

/// <summary>
/// A parameter value actually used by a step, recorded in the execution log so a run can be
/// diagnosed after the fact (feature 078, FR-024). Values are not redacted: they are device serials,
/// instance names and timings, and diagnosability is the point.
/// </summary>
/// <param name="Name">Parameter name.</param>
/// <param name="Value">The resolved value.</param>
/// <param name="OriginLayer">Which scope layer supplied it; one of <see cref="ParameterScopeLayers"/>.</param>
public sealed record ResolvedParameter(string Name, string Value, string OriginLayer);
