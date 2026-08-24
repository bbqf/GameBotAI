using System.Collections.Generic;
using System.Linq;
using GameBot.Domain.Commands;
using GameBot.Domain.Parameters;
using GameBot.Domain.QueueTemplates;
using GameBot.Domain.Utils;

namespace GameBot.Domain.Services;

/// <summary>Canonical machine-readable codes for parameter validation feedback (feature 078).</summary>
public static class ParameterValidationCodes {
  /// <summary>A declaration's name is malformed, reserved, or duplicated.</summary>
  public const string InvalidDeclaration = "invalid_parameter_declaration";

  /// <summary>A numeric declaration's default does not parse as a whole number.</summary>
  public const string InvalidDefault = "invalid_parameter_default";

  /// <summary>A field-template key is not one of the supported numeric field paths.</summary>
  public const string UnknownFieldTemplatePath = "unknown_field_template_path";

  /// <summary>A reference names something neither declared here nor a queue built-in.</summary>
  public const string UnresolvableReference = "unresolvable_parameter_reference";

  /// <summary>A call site binds a name the callee does not declare.</summary>
  public const string UnknownBinding = "unknown_parameter_binding";

  /// <summary>A placeholder appears in a command/sequence reference field, which must stay literal.</summary>
  public const string ParameterInReferenceField = "parameter_in_reference_field";

  /// <summary>An ad-hoc template-entry value name is malformed or reserved.</summary>
  public const string InvalidValueName = "invalid_parameter_value_name";

  /// <summary>Warning: a field's static existence check was skipped because it is parametrized.</summary>
  public const string StaticCheckSkipped = "static_check_skipped";

  /// <summary>Warning: a supplied value is consumed by nothing in the entry's reachable chain.</summary>
  public const string UnusedValue = "unused_parameter_value";

  /// <summary>Warning: a binding names a parameter the callee no longer declares.</summary>
  public const string StaleBinding = "stale_parameter_binding";

  /// <summary>Warning/blocker: a required parameter cannot be supplied by anything in scope.</summary>
  public const string UnsatisfiedRequired = "unsatisfied_required_parameter";
}

/// <summary>One piece of parameter validation feedback.</summary>
/// <param name="Code">One of <see cref="ParameterValidationCodes"/>.</param>
/// <param name="Message">Operator-facing text naming what is wrong and where.</param>
/// <param name="FieldPath">Dotted field path the problem sits at, when applicable.</param>
/// <param name="ParameterName">Parameter the problem concerns, when applicable.</param>
/// <param name="EntryIndex">Queue-template entry index, for template-level feedback.</param>
public sealed record ParameterValidationIssue(
    string Code,
    string Message,
    string? FieldPath = null,
    string? ParameterName = null,
    int? EntryIndex = null);

/// <summary>Errors block the operation; warnings are reported but never block.</summary>
/// <param name="Errors">Blocking problems.</param>
/// <param name="Warnings">Non-blocking advisories.</param>
public sealed record ParameterValidationResult(
    IReadOnlyList<ParameterValidationIssue> Errors,
    IReadOnlyList<ParameterValidationIssue> Warnings) {
  /// <summary>True when nothing blocks the operation.</summary>
  public bool IsValid => Errors.Count == 0;

  /// <summary>An empty, passing result.</summary>
  public static ParameterValidationResult Ok { get; } =
      new(System.Array.Empty<ParameterValidationIssue>(), System.Array.Empty<ParameterValidationIssue>());
}

/// <summary>
/// Save-time and pre-run validation for parameters (feature 078).
/// <para>
/// The split matters: a save-time check cannot know what values a future queue run will supply, so it
/// judges only what is statically knowable — a reference must name something this entity declares, a
/// queue built-in, or the loop <c>iteration</c> inside a loop. Everything else is caught when a queue
/// starts, or, failing that, at dispatch, where an unresolved name fails the step instead of reaching
/// the device.
/// </para>
/// </summary>
public static class ParameterValidationService {
  /// <summary>Validates a command's declarations, field templates and references.</summary>
  /// <param name="command">Command to validate.</param>
  public static ParameterValidationResult ValidateCommand(Command command) {
    ArgumentNullException.ThrowIfNull(command);
    var errors = new List<ParameterValidationIssue>();
    var warnings = new List<ParameterValidationIssue>();

    AddDeclarationIssues(command.Parameters, errors);

    foreach (var step in command.Steps) {
      ValidateFieldTemplateKeys(step, errors);

      // A placeholder in a Command step's TargetId would defeat the dangling-reference check.
      if (step.Type == CommandStepType.Command && TemplateSubstitutor.ContainsPlaceholder(step.TargetId)) {
        errors.Add(new ParameterValidationIssue(
            ParameterValidationCodes.ParameterInReferenceField,
            $"Step {step.Order}: the target command reference must be a literal id, not a parameter.",
            "targetId"));
      }
    }

    var declared = NamesOf(command.Parameters);
    foreach (var reference in ParameterReferenceScanner.Scan(command)) {
      AddReferenceIssues(reference, declared, errors, warnings);
    }

    return new ParameterValidationResult(errors, warnings);
  }

  /// <summary>
  /// Validates a sequence's declarations and references, plus each command step's bindings against the
  /// command it invokes.
  /// </summary>
  /// <param name="sequence">Sequence to validate.</param>
  /// <param name="commandLookup">Resolves a command id to its declarations; returns null when unknown.</param>
  public static ParameterValidationResult ValidateSequence(
      CommandSequence sequence,
      Func<string, IReadOnlyList<ParameterDeclaration>?> commandLookup) {
    ArgumentNullException.ThrowIfNull(sequence);
    ArgumentNullException.ThrowIfNull(commandLookup);
    var errors = new List<ParameterValidationIssue>();
    var warnings = new List<ParameterValidationIssue>();

    AddDeclarationIssues(sequence.Parameters, errors);

    var declared = NamesOf(sequence.Parameters);
    foreach (var reference in ParameterReferenceScanner.Scan(sequence)) {
      AddReferenceIssues(reference, declared, errors, warnings);
    }

    foreach (var step in FlattenSteps(sequence.Steps)) {
      if (step.ParameterBindings is not { Count: > 0 }) continue;
      var calleeDeclarations = commandLookup(step.CommandId);
      if (calleeDeclarations is null) continue; // unknown command is reported by existing validation

      var calleeNames = NamesOf(calleeDeclarations);
      foreach (var binding in step.ParameterBindings) {
        if (binding?.Name is null || calleeNames.Contains(binding.Name)) continue;
        errors.Add(new ParameterValidationIssue(
            ParameterValidationCodes.UnknownBinding,
            $"Step '{StepLabel(step)}' binds '{binding.Name}', which command '{step.CommandId}' does not declare.",
            $"parameterBindings.{binding.Name}",
            binding.Name));
      }
    }

    return new ParameterValidationResult(errors, warnings);
  }

  /// <summary>
  /// Validates one queue-template entry's supplied values against the sequence it references and the
  /// commands reachable beneath it.
  /// </summary>
  /// <param name="entry">Entry to validate.</param>
  /// <param name="entryIndex">Index of the entry, echoed back so the UI can anchor the feedback.</param>
  /// <param name="sequence">The referenced sequence, or null when the reference is stale.</param>
  /// <param name="reachableDeclarations">Declarations of every command reachable from the sequence.</param>
  /// <param name="queueSuppliedNames">Names the target queue's built-ins can supply, if known.</param>
  public static ParameterValidationResult ValidateTemplateEntry(
      QueueTemplateEntry entry,
      int entryIndex,
      CommandSequence? sequence,
      IReadOnlyList<ParameterDeclaration> reachableDeclarations,
      IReadOnlyCollection<string>? queueSuppliedNames = null) {
    ArgumentNullException.ThrowIfNull(entry);
    ArgumentNullException.ThrowIfNull(reachableDeclarations);
    var errors = new List<ParameterValidationIssue>();
    var warnings = new List<ParameterValidationIssue>();

    var suppliedNames = new HashSet<string>(StringComparer.Ordinal);
    foreach (var binding in entry.ParameterValues) {
      var nameError = ParameterNameRules.ValidateName(binding?.Name);
      if (nameError is not null) {
        errors.Add(new ParameterValidationIssue(
            ParameterValidationCodes.InvalidValueName,
            $"Entry {entryIndex}: {nameError}.",
            null,
            binding?.Name,
            entryIndex));
        continue;
      }

      suppliedNames.Add(binding!.Name);
    }

    // Every name anything under this entry could consume: the sequence's own declarations plus every
    // reachable command's. An ad-hoc value matching none of them is consumed by nothing.
    var consumable = new HashSet<string>(StringComparer.Ordinal);
    if (sequence is not null) foreach (var name in NamesOf(sequence.Parameters)) consumable.Add(name);
    foreach (var name in NamesOf(reachableDeclarations)) consumable.Add(name);

    foreach (var name in suppliedNames) {
      if (consumable.Contains(name)) continue;
      warnings.Add(new ParameterValidationIssue(
          ParameterValidationCodes.UnusedValue,
          $"Entry {entryIndex}: value '{name}' is not used by anything in this entry.",
          null,
          name,
          entryIndex));
    }

    // A required parameter must be satisfiable from this entry, the queue built-ins, or its default.
    foreach (var declaration in AllDeclarations(sequence, reachableDeclarations)) {
      if (!declaration.Required) continue;
      if (suppliedNames.Contains(declaration.Name)) continue;
      if (declaration.Default is not null) continue;
      if (queueSuppliedNames is not null && queueSuppliedNames.Contains(declaration.Name)) continue;

      warnings.Add(new ParameterValidationIssue(
          ParameterValidationCodes.UnsatisfiedRequired,
          $"Entry {entryIndex}: required parameter '{declaration.Name}' has no value and no default.",
          null,
          declaration.Name,
          entryIndex));
    }

    return new ParameterValidationResult(errors, warnings);
  }

  /// <summary>
  /// The required parameters an entry cannot supply, used to refuse a queue start before any device
  /// work happens (FR-022). Empty means the entry is safe to run.
  /// </summary>
  /// <param name="entry">Entry to check.</param>
  /// <param name="sequence">The referenced sequence, or null when stale.</param>
  /// <param name="reachableDeclarations">Declarations of every reachable command.</param>
  /// <param name="queueSuppliedNames">Names the queue's built-ins supply.</param>
  public static IReadOnlyList<string> FindUnsatisfiedRequired(
      QueueTemplateEntry entry,
      CommandSequence? sequence,
      IReadOnlyList<ParameterDeclaration> reachableDeclarations,
      IReadOnlyCollection<string> queueSuppliedNames) {
    ArgumentNullException.ThrowIfNull(entry);
    ArgumentNullException.ThrowIfNull(reachableDeclarations);
    ArgumentNullException.ThrowIfNull(queueSuppliedNames);

    var supplied = entry.ParameterValues
        .Where(b => b?.Name is not null && b.Value is not null)
        .Select(b => b.Name)
        .ToHashSet(StringComparer.Ordinal);

    return AllDeclarations(sequence, reachableDeclarations)
        .Where(d => d.Required
            && d.Default is null
            && !supplied.Contains(d.Name)
            && !queueSuppliedNames.Contains(d.Name))
        .Select(d => d.Name)
        .Distinct(StringComparer.Ordinal)
        .ToList();
  }

  private static IEnumerable<ParameterDeclaration> AllDeclarations(
      CommandSequence? sequence,
      IReadOnlyList<ParameterDeclaration> reachableDeclarations) {
    if (sequence is not null) {
      foreach (var declaration in sequence.Parameters) yield return declaration;
    }

    foreach (var declaration in reachableDeclarations) yield return declaration;
  }

  private static void AddDeclarationIssues(
      IEnumerable<ParameterDeclaration> declarations,
      List<ParameterValidationIssue> errors) {
    foreach (var error in ParameterNameRules.ValidateDeclarations(declarations)) {
      var code = error.Contains("is not a whole number", StringComparison.Ordinal)
          ? ParameterValidationCodes.InvalidDefault
          : ParameterValidationCodes.InvalidDeclaration;
      errors.Add(new ParameterValidationIssue(code, error));
    }
  }

  private static void ValidateFieldTemplateKeys(CommandStep step, List<ParameterValidationIssue> errors) {
    if (step.FieldTemplates is null) return;
    foreach (var path in step.FieldTemplates.Keys) {
      if (CommandStepFieldPaths.IsSupported(path)) continue;
      errors.Add(new ParameterValidationIssue(
          ParameterValidationCodes.UnknownFieldTemplatePath,
          $"Step {step.Order}: '{path}' is not a parametrizable numeric field.",
          path));
    }
  }

  private static void AddReferenceIssues(
      ParameterReference reference,
      HashSet<string> declared,
      List<ParameterValidationIssue> errors,
      List<ParameterValidationIssue> warnings) {
    if (reference.DefeatsStaticCheck) {
      warnings.Add(new ParameterValidationIssue(
          ParameterValidationCodes.StaticCheckSkipped,
          $"Step '{reference.StepLabel}': '{reference.FieldPath}' is parametrized, so its target is checked at run time instead of now.",
          reference.FieldPath,
          reference.ParameterName));
    }

    if (declared.Contains(reference.ParameterName)) return;
    if (ParameterNameRules.IsBuiltIn(reference.ParameterName)) return;

    // {{iteration}} keeps its original rule: meaningful inside a loop, rejected anywhere else.
    if (string.Equals(reference.ParameterName, ParameterNameRules.IterationName, StringComparison.Ordinal)) {
      if (reference.InsideLoop) return;
      errors.Add(new ParameterValidationIssue(
          ParameterValidationCodes.UnresolvableReference,
          $"Step '{reference.StepLabel}': '{{{{{ParameterNameRules.IterationName}}}}}' is only valid inside a loop body.",
          reference.FieldPath,
          reference.ParameterName));
      return;
    }

    errors.Add(new ParameterValidationIssue(
        ParameterValidationCodes.UnresolvableReference,
        $"Step '{reference.StepLabel}': '{reference.ParameterName}' is not declared here and is not a queue built-in.",
        reference.FieldPath,
        reference.ParameterName));
  }

  private static HashSet<string> NamesOf(IEnumerable<ParameterDeclaration> declarations) =>
      declarations.Where(d => d?.Name is not null).Select(d => d.Name).ToHashSet(StringComparer.Ordinal);

  private static string StepLabel(SequenceStep step) =>
      string.IsNullOrWhiteSpace(step.StepId)
          ? step.Order.ToString(System.Globalization.CultureInfo.InvariantCulture)
          : step.StepId;

  private static IEnumerable<SequenceStep> FlattenSteps(IEnumerable<SequenceStep> steps) {
    foreach (var step in steps) {
      yield return step;
      foreach (var child in FlattenSteps(step.Body)) yield return child;
      if (step.ElseBody is null) continue;
      foreach (var child in FlattenSteps(step.ElseBody)) yield return child;
    }
  }
}
