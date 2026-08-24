using System.Collections.Generic;
using System.Linq;
using GameBot.Domain.Commands;
using GameBot.Domain.Utils;

namespace GameBot.Domain.Parameters;

/// <summary>One <c>{{name}}</c> reference found in an entity, with the field that contained it.</summary>
/// <param name="ParameterName">The referenced name.</param>
/// <param name="FieldPath">Dotted path of the containing field, e.g. <c>steps[0].ensureEmulatorRunning.adbSerial</c>.</param>
/// <param name="StepLabel">Step id or order the field belongs to; empty for entity-level fields.</param>
/// <param name="InsideLoop">Whether the field sits inside a loop body, which is what makes <c>iteration</c> legal.</param>
/// <param name="DefeatsStaticCheck">
/// True when the field normally undergoes static existence checking — an image or detection
/// reference — so parametrizing it means that check must be deferred to run time.
/// </param>
public sealed record ParameterReference(
    string ParameterName,
    string FieldPath,
    string StepLabel,
    bool InsideLoop = false,
    bool DefeatsStaticCheck = false);

/// <summary>
/// Finds every parameter reference in a command or a sequence (feature 078), so validation can decide
/// whether each one is satisfiable and the authoring UI can report unused values.
/// </summary>
public static class ParameterReferenceScanner {
  /// <summary>Scans a command's steps and detection target for parameter references.</summary>
  /// <param name="command">Command to scan; <c>null</c> yields an empty list.</param>
  public static IReadOnlyList<ParameterReference> Scan(Command? command) {
    var found = new List<ParameterReference>();
    if (command is null) return found;

    AddFrom(found, command.Detection?.ReferenceImageId, "detection.referenceImageId", string.Empty,
        defeatsStaticCheck: true);

    foreach (var step in command.Steps) {
      ScanCommandStep(found, step);
    }

    return found;
  }

  /// <summary>Scans a sequence's steps, including loop and if bodies, for parameter references.</summary>
  /// <param name="sequence">Sequence to scan; <c>null</c> yields an empty list.</param>
  public static IReadOnlyList<ParameterReference> Scan(CommandSequence? sequence) {
    var found = new List<ParameterReference>();
    if (sequence is null) return found;
    ScanSequenceSteps(found, sequence.Steps, insideLoop: false);
    return found;
  }

  /// <summary>Scans a single command step, used by both the command scan and per-step validation.</summary>
  /// <param name="found">Accumulator the discovered references are appended to.</param>
  /// <param name="step">Step to scan.</param>
  public static void ScanCommandStep(ICollection<ParameterReference> found, CommandStep? step) {
    ArgumentNullException.ThrowIfNull(found);
    if (step is null) return;
    var label = step.Order.ToString(System.Globalization.CultureInfo.InvariantCulture);

    // TargetId is a *command reference* on Command steps and must stay literal, so it is scanned
    // only for the step types where it is a plain value.
    if (step.Type != CommandStepType.Command) {
      AddFrom(found, step.TargetId, "targetId", label);
    }

    AddFrom(found, step.KeyInput?.Key, "keyInput.key", label);
    AddFrom(found, step.EnsureEmulatorRunning?.AdbSerial, "ensureEmulatorRunning.adbSerial", label);
    AddFrom(found, step.EnsureEmulatorRunning?.InstanceName, "ensureEmulatorRunning.instanceName", label);
    AddFrom(found, step.PrimitiveTap?.DetectionTarget?.ReferenceImageId,
        "primitiveTap.detectionTarget.referenceImageId", label, defeatsStaticCheck: true);
    AddFrom(found, step.WaitForImage?.DetectionTarget?.ReferenceImageId,
        "waitForImage.detectionTarget.referenceImageId", label, defeatsStaticCheck: true);
    AddFrom(found, step.EnsureGameRunning?.ReadinessImage?.ReferenceImageId,
        "ensureGameRunning.readinessImage.referenceImageId", label, defeatsStaticCheck: true);

    if (step.FieldTemplates is null) return;
    foreach (var (path, template) in step.FieldTemplates) {
      AddFrom(found, template, path, label);
    }
  }

  private static void ScanSequenceSteps(
      ICollection<ParameterReference> found,
      IReadOnlyList<SequenceStep> steps,
      bool insideLoop) {
    foreach (var step in steps) {
      var label = string.IsNullOrWhiteSpace(step.StepId)
          ? step.Order.ToString(System.Globalization.CultureInfo.InvariantCulture)
          : step.StepId;

      if (step.Action is not null) {
        foreach (var (key, value) in step.Action.Parameters) {
          if (value is string text) AddFrom(found, text, $"action.{key}", label, insideLoop);
        }
      }

      if (step.ParameterBindings is not null) {
        foreach (var binding in step.ParameterBindings) {
          AddFrom(found, binding?.Value, $"parameterBindings.{binding?.Name}", label, insideLoop);
        }
      }

      // Loop bodies make {{iteration}} legal; if branches inherit whatever their parent allowed.
      var bodyInsideLoop = insideLoop || step.StepType == SequenceStepType.Loop;
      if (step.Body.Count > 0) ScanSequenceSteps(found, step.Body, bodyInsideLoop);
      if (step.ElseBody is { Count: > 0 }) ScanSequenceSteps(found, step.ElseBody, bodyInsideLoop);
    }
  }

  private static void AddFrom(
      ICollection<ParameterReference> found,
      string? text,
      string fieldPath,
      string stepLabel,
      bool insideLoop = false,
      bool defeatsStaticCheck = false) {
    if (!TemplateSubstitutor.ContainsPlaceholder(text)) return;
    foreach (var key in TemplateSubstitutor.ExtractKeys(text)) {
      found.Add(new ParameterReference(key, fieldPath, stepLabel, insideLoop, defeatsStaticCheck));
    }
  }

  /// <summary>
  /// The distinct names referenced by a collection of references, used to decide whether a supplied
  /// value is consumed by anything.
  /// </summary>
  /// <param name="references">References to project.</param>
  public static IReadOnlyCollection<string> DistinctNames(IEnumerable<ParameterReference> references) {
    ArgumentNullException.ThrowIfNull(references);
    return references.Select(r => r.ParameterName).Distinct(System.StringComparer.Ordinal).ToList();
  }
}
