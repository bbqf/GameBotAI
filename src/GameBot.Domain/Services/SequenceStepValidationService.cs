using GameBot.Domain.Commands;
using GameBot.Domain.Commands.SelfReschedule;
using GameBot.Domain.Actions;
using System.Linq;
using System.Text.RegularExpressions;
using System.Text.Json;

namespace GameBot.Domain.Services;

public sealed class SequenceStepValidationService {
  private readonly StringComparer _stepIdComparer = StringComparer.Ordinal;
  private static readonly HashSet<string> AllowedCommandOutcomeStates = new(StringComparer.OrdinalIgnoreCase) {
    "success",
    "failed",
    "skipped"
  };
  private static readonly HashSet<string> AllowedPrimitiveActionTypes = new(PrimitiveActionTypes.All, StringComparer.OrdinalIgnoreCase);

  // Matches {{word}} placeholders used for template substitution.
  private static readonly Regex TemplatePlaceholder =
      new(@"\{\{\w+\}\}", RegexOptions.Compiled, TimeSpan.FromMilliseconds(500));

  public IReadOnlyList<string> Validate(IReadOnlyList<SequenceStep> steps) {
    ArgumentNullException.ThrowIfNull(steps);

    var errors = new List<string>();
    var seenStepIds = new HashSet<string>(_stepIdComparer);

    for (var index = 0; index < steps.Count; index++) {
      var step = steps[index];
      var stepLabel = string.IsNullOrWhiteSpace(step.StepId) ? $"index:{index}" : step.StepId;

      if (string.IsNullOrWhiteSpace(step.StepId)) {
        errors.Add($"Step at index {index} requires non-empty stepId.");
      }
      else if (!seenStepIds.Add(step.StepId)) {
        errors.Add($"Duplicate step id '{step.StepId}'.");
      }

      if (step.StepType == SequenceStepType.Break) {
        // Top-level break steps are never valid.
        errors.Add($"Step '{stepLabel}' has stepType 'Break' which is only valid inside a loop body.");
        continue;
      }

      if (step.StepType == SequenceStepType.Loop) {
        ValidateLoopStep(step, stepLabel, steps, index, errors, insideLoop: false);
        continue;
      }

      if (step.StepType == SequenceStepType.If) {
        ValidateIfStep(step, stepLabel, errors, insideLoop: false);
        continue;
      }

      if (step.Action is null) {
        errors.Add($"Step '{stepLabel}' requires action payload.");
      }

      ValidateStepCondition(step, stepLabel, steps, index, errors, insideLoop: false);
    }

    return errors;
  }

  private void ValidateLoopStep(
      SequenceStep step,
      string stepLabel,
      IReadOnlyList<SequenceStep> siblings,
      int indexInSiblings,
      List<string> errors,
      bool insideLoop) {

    if (step.Loop is null) {
      errors.Add($"Loop step '{stepLabel}' requires a loop configuration.");
      return;
    }

    // FR-008: MaxIterations must be > 0 when set.
    if (step.Loop.MaxIterations.HasValue && step.Loop.MaxIterations.Value <= 0) {
      errors.Add($"Loop step '{stepLabel}' maxIterations must be greater than zero.");
    }

    // FR-004: count must be >= 0.
    if (step.Loop is CountLoopConfig countCfg && countCfg.Count < 0) {
      errors.Add($"Loop step '{stepLabel}' count must be zero or greater.");
    }

    // Validate body steps.
    var bodyStepIds = new HashSet<string>(_stepIdComparer);
    for (var bi = 0; bi < step.Body.Count; bi++) {
      var bodyStep = step.Body[bi];
      var bodyLabel = string.IsNullOrWhiteSpace(bodyStep.StepId) ? $"{stepLabel}.body[{bi}]" : bodyStep.StepId;

      if (string.IsNullOrWhiteSpace(bodyStep.StepId)) {
        errors.Add($"Body step at index {bi} inside '{stepLabel}' requires non-empty stepId.");
      }
      else if (!bodyStepIds.Add(bodyStep.StepId)) {
        errors.Add($"Duplicate body step id '{bodyStep.StepId}' inside loop '{stepLabel}'.");
      }

      // FR-012: nested loops are forbidden.
      if (bodyStep.StepType == SequenceStepType.Loop) {
        errors.Add($"Body step '{bodyLabel}' inside loop '{stepLabel}' must not itself be a loop step.");
        continue;
      }

      // Feature 067: loop bodies may contain if steps (whose branches may then use breaks).
      if (bodyStep.StepType == SequenceStepType.If) {
        ValidateIfStep(bodyStep, bodyLabel, errors, insideLoop: true);
        continue;
      }

      if (bodyStep.StepType == SequenceStepType.Break) {
        if (bodyStep.BreakCondition is ImageVisibleStepCondition brkImgVis
            && string.IsNullOrWhiteSpace(brkImgVis.ImageId)) {
          errors.Add($"Break step '{bodyLabel}' imageVisible breakCondition requires imageId.");
        }
        continue;
      }

      if (bodyStep.Action is null) {
        errors.Add($"Body step '{bodyLabel}' inside loop '{stepLabel}' requires action payload.");
      }

      // FR-002a: {{iteration}} (or any template placeholder) is only valid inside a loop body —
      // validate that top-level steps do NOT contain placeholders (done in Validate()). Here
      // we just validate the body step's condition references.
      ValidateStepCondition(bodyStep, bodyLabel, step.Body, bi, errors, insideLoop: true);
    }
  }

  // Feature 067: validates an if step and its then/else branches. Branches follow loop-body
  // rules (flat: no loops, no nested ifs); breaks are only allowed when the if step itself
  // sits inside a loop body.
  private void ValidateIfStep(
      SequenceStep step,
      string stepLabel,
      List<string> errors,
      bool insideLoop) {

    if (step.If is null) {
      errors.Add($"If step '{stepLabel}' requires an if configuration with a condition.");
    }
    else {
      ValidateIfCondition(step.If.Condition, stepLabel, errors);
    }

    ValidateIfBranch(step.Body, "then", stepLabel, errors, insideLoop);
    if (step.ElseBody is not null) {
      ValidateIfBranch(step.ElseBody, "else", stepLabel, errors, insideLoop);
    }
  }

  private static void ValidateIfCondition(SequenceStepCondition condition, string stepLabel, List<string> errors) {
    if (condition is ImageVisibleStepCondition imageVisible && string.IsNullOrWhiteSpace(imageVisible.ImageId)) {
      errors.Add($"Step '{stepLabel}' imageVisible condition requires imageId.");
    }

    if (condition is CommandOutcomeStepCondition commandOutcome) {
      if (string.IsNullOrWhiteSpace(commandOutcome.StepRef)) {
        errors.Add($"Step '{stepLabel}' commandOutcome condition requires stepRef.");
      }

      if (string.IsNullOrWhiteSpace(commandOutcome.ExpectedState)
          || !AllowedCommandOutcomeStates.Contains(commandOutcome.ExpectedState)) {
        errors.Add($"Step '{stepLabel}' commandOutcome expectedState must be one of success|failed|skipped.");
      }
    }
  }

  private void ValidateIfBranch(
      IReadOnlyList<SequenceStep> branch,
      string branchName,
      string stepLabel,
      List<string> errors,
      bool insideLoop) {

    var branchStepIds = new HashSet<string>(_stepIdComparer);
    for (var bi = 0; bi < branch.Count; bi++) {
      var branchStep = branch[bi];
      var branchLabel = string.IsNullOrWhiteSpace(branchStep.StepId) ? $"{stepLabel}.{branchName}[{bi}]" : branchStep.StepId;

      if (string.IsNullOrWhiteSpace(branchStep.StepId)) {
        errors.Add($"Branch step at index {bi} inside if '{stepLabel}' ({branchName}) requires non-empty stepId.");
      }
      else if (!branchStepIds.Add(branchStep.StepId)) {
        errors.Add($"Duplicate branch step id '{branchStep.StepId}' inside if '{stepLabel}' ({branchName}).");
      }

      // Branches are flat, mirroring the loop-body rule.
      if (branchStep.StepType == SequenceStepType.Loop) {
        errors.Add($"Branch step '{branchLabel}' inside if '{stepLabel}' must not itself be a loop step.");
        continue;
      }

      if (branchStep.StepType == SequenceStepType.If) {
        errors.Add($"Branch step '{branchLabel}' inside if '{stepLabel}' must not itself be an if step.");
        continue;
      }

      if (branchStep.StepType == SequenceStepType.Break) {
        if (!insideLoop) {
          errors.Add($"Branch step '{branchLabel}' inside if '{stepLabel}' has stepType 'Break' which is only valid inside a loop body.");
        }
        else if (branchStep.BreakCondition is ImageVisibleStepCondition brkImgVis
            && string.IsNullOrWhiteSpace(brkImgVis.ImageId)) {
          errors.Add($"Break step '{branchLabel}' imageVisible breakCondition requires imageId.");
        }
        continue;
      }

      if (branchStep.Action is null) {
        errors.Add($"Branch step '{branchLabel}' inside if '{stepLabel}' requires action payload.");
      }

      ValidateStepCondition(branchStep, branchLabel, branch, bi, errors, insideLoop: insideLoop);
    }
  }

  private static void ValidateStepCondition(
      SequenceStep step,
      string stepLabel,
      IReadOnlyList<SequenceStep> siblings,
      int indexInSiblings,
      List<string> errors,
      bool insideLoop) {

    if (step.Action is not null) {
      if (string.Equals(step.Action.Type, ActionTypes.RescheduleSelf, StringComparison.OrdinalIgnoreCase)) {
        // feature 065: a self-reschedule action carries an option (+ optional Timer fields).
        ValidateRescheduleSelfPayload(step.Action, stepLabel, errors);
      }
      else if (string.IsNullOrWhiteSpace(step.Action.Type) || !AllowedPrimitiveActionTypes.Contains(step.Action.Type)) {
        errors.Add($"Step '{stepLabel}' action type '{step.Action.Type}' is not a supported primitive action type.");
      }
      else if (string.Equals(step.Action.Type, PrimitiveActionTypes.WaitForImage, StringComparison.OrdinalIgnoreCase)) {
        ValidateWaitForImagePayload(step.Action.Parameters, stepLabel, errors);
      }
    }

    // FR-002a: {{iteration}} placeholders in action parameters are only permitted inside loops.
    if (!insideLoop && step.Action is not null) {
      foreach (var paramValue in step.Action.Parameters.Values) {
        if (paramValue is string s && TemplatePlaceholder.IsMatch(s)) {
          errors.Add($"Step '{stepLabel}' contains template placeholder(s) in action parameters which are only valid inside a loop body.");
          break;
        }
      }
    }

    if (step.Condition is CommandOutcomeStepCondition commandOutcome) {
      if (string.IsNullOrWhiteSpace(commandOutcome.StepRef)) {
        errors.Add($"Step '{stepLabel}' commandOutcome condition requires stepRef.");
      }
      else {
        var referencedIndex = siblings
          .Select((candidate, candidateIndex) => new { candidate.StepId, candidateIndex })
          .FirstOrDefault(candidate => string.Equals(candidate.StepId, commandOutcome.StepRef, StringComparison.Ordinal))
          ?.candidateIndex ?? -1;

        if (referencedIndex < 0) {
          errors.Add($"Step '{stepLabel}' commandOutcome references unknown prior step '{commandOutcome.StepRef}'.");
        }
        else if (referencedIndex >= indexInSiblings) {
          // FR-006: inside a loop body a commandOutcome must not forward-reference within the same body.
          errors.Add($"Step '{stepLabel}' commandOutcome stepRef '{commandOutcome.StepRef}' must reference a prior step.");
        }
      }

      if (string.IsNullOrWhiteSpace(commandOutcome.ExpectedState)
          || !AllowedCommandOutcomeStates.Contains(commandOutcome.ExpectedState)) {
        errors.Add($"Step '{stepLabel}' commandOutcome expectedState must be one of success|failed|skipped.");
      }
    }

    if (step.Condition is ImageVisibleStepCondition imageVisible
        && string.IsNullOrWhiteSpace(imageVisible.ImageId)) {
      errors.Add($"Step '{stepLabel}' imageVisible condition requires imageId.");
    }
  }

  // feature 065: validates a reschedule-self action payload, mirroring the queue-template timer rules.
  private static readonly TimeSpan MaxRescheduleOffset = TimeSpan.FromHours(24);

  private static void ValidateRescheduleSelfPayload(
      SequenceActionPayload action,
      string stepLabel,
      List<string> errors) {
    if (!SelfReschedulePayload.TryRead(action, out var payload, out var parseError) || payload is null) {
      errors.Add($"Step '{stepLabel}' reschedule-self action is invalid: {parseError}");
      return;
    }

    if (payload.Option == SelfRescheduleOption.Timer) {
      if (payload.HasOcrOffset) {
        // feature 068: the ocrOffset spec supplies the offset at runtime (with a required static
        // fallback), so the static timerTimeOfDay/timerRelativeOffset fields are optional here.
        ValidateOcrOffset(payload.OcrOffset!, stepLabel, errors);
      }
      else if (payload.HasTimerTimeOfDay && payload.HasTimerRelativeOffset) {
        errors.Add($"Step '{stepLabel}' reschedule-self Timer requires exactly one of timerTimeOfDay or timerRelativeOffset, not both.");
      }
      else if (!payload.HasTimerTimeOfDay && !payload.HasTimerRelativeOffset) {
        errors.Add($"Step '{stepLabel}' reschedule-self Timer requires a timerTimeOfDay or timerRelativeOffset.");
      }

      if (payload.TimerRelativeOffset is { } offset && (offset < TimeSpan.Zero || offset > MaxRescheduleOffset)) {
        errors.Add($"Step '{stepLabel}' reschedule-self timerRelativeOffset must be between 00:00:00 and 24:00:00.");
      }
    }
    else if (payload.HasTimerTimeOfDay || payload.HasTimerRelativeOffset) {
      errors.Add($"Step '{stepLabel}' reschedule-self timer fields are only valid when option is Timer.");
    }
    else if (payload.HasOcrOffset) {
      errors.Add($"Step '{stepLabel}' reschedule-self ocrOffset is only valid when option is Timer.");
    }
  }

  // feature 068: cross-field rules for an ocrOffset spec (region present positive; min < max). The
  // fallback presence/format is enforced at parse time in SelfReschedulePayload.TryRead.
  private static void ValidateOcrOffset(
      SelfRescheduleOcrOffset ocrOffset,
      string stepLabel,
      List<string> errors) {
    if (ocrOffset.Region.Width <= 0 || ocrOffset.Region.Height <= 0) {
      errors.Add($"Step '{stepLabel}' reschedule-self ocrOffset.region must have positive width and height.");
    }

    if (ocrOffset.Min >= ocrOffset.Max) {
      errors.Add($"Step '{stepLabel}' reschedule-self ocrOffset.min must be less than ocrOffset.max.");
    }

    if (ocrOffset.Fallback < TimeSpan.Zero || ocrOffset.Fallback > MaxRescheduleOffset) {
      errors.Add($"Step '{stepLabel}' reschedule-self ocrOffset.fallback must be between 00:00:00 and 24:00:00.");
    }
  }

  private static void ValidateWaitForImagePayload(
      Dictionary<string, object?> parameters,
      string stepLabel,
      List<string> errors) {
    if (parameters.TryGetValue("timeoutMs", out var timeoutValue)
        && TryReadInt(timeoutValue, out var timeoutMs)
        && timeoutMs < 0) {
      errors.Add($"Step '{stepLabel}' waitForImage timeoutMs must be greater than or equal to zero.");
    }

    if (!parameters.TryGetValue("detectionTarget", out var detectionTargetValue)) {
      return;
    }

    if (TryReadJsonObject(detectionTargetValue, out var detectionTarget)
        && detectionTarget.TryGetProperty("referenceImageId", out var referenceImageId)
        && referenceImageId.ValueKind == JsonValueKind.String
        && string.IsNullOrWhiteSpace(referenceImageId.GetString())) {
      errors.Add($"Step '{stepLabel}' waitForImage detectionTarget.referenceImageId must not be empty when detectionTarget is provided.");
    }
  }

  private static bool TryReadInt(object? value, out int result) {
    switch (value) {
      case int intValue:
        result = intValue;
        return true;
      case long longValue when longValue is >= int.MinValue and <= int.MaxValue:
        result = (int)longValue;
        return true;
      case JsonElement element when element.ValueKind == JsonValueKind.Number && element.TryGetInt32(out var parsedInt):
        result = parsedInt;
        return true;
      case string text when int.TryParse(text, out var parsedText):
        result = parsedText;
        return true;
      default:
        result = 0;
        return false;
    }
  }

  private static bool TryReadJsonObject(object? value, out JsonElement element) {
    if (value is JsonElement jsonElement && jsonElement.ValueKind == JsonValueKind.Object) {
      element = jsonElement;
      return true;
    }

    element = default;
    return false;
  }

  public IReadOnlyList<string> Validate(SequenceFlowGraph graph) {
    ArgumentNullException.ThrowIfNull(graph);

    var errors = new List<string>();
    var seenStepIds = new HashSet<string>(_stepIdComparer);
    foreach (var step in graph.Steps) {
      if (!string.IsNullOrWhiteSpace(step.StepId) && !seenStepIds.Add(step.StepId)) {
        errors.Add($"Duplicate step id '{step.StepId}'.");
      }

      switch (step.StepType) {
        case FlowStepType.Action:
        case FlowStepType.Command:
          if (string.IsNullOrWhiteSpace(step.PayloadRef)) {
            errors.Add($"Step '{step.StepId}' requires payloadRef for step type '{step.StepType}'.");
          }
          break;
        case FlowStepType.Condition:
          if (step.Condition is null) {
            errors.Add($"Condition step '{step.StepId}' requires a condition expression.");
          }
          break;
        case FlowStepType.Terminal:
          if (!string.IsNullOrWhiteSpace(step.PayloadRef)) {
            errors.Add($"Terminal step '{step.StepId}' must not define payloadRef.");
          }
          break;
      }
    }

    return errors;
  }
}
