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

  /// <summary>A field-template key is not one of the supported field paths.</summary>
  public const string UnknownFieldTemplatePath = "unknown_field_template_path";

  /// <summary>
  /// The value of a field-template image key is not one whole placeholder, for example <c>{{name}}</c>
  /// (feature 114).
  /// </summary>
  public const string InvalidFieldTemplateValue = "invalid_field_template_value";

  /// <summary>
  /// A known template entry value (or default) goes to an image field, but no image has that id
  /// (feature 114).
  /// </summary>
  public const string UnknownImageReference = "unknown_image_reference";

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

/// <summary>
/// An image id that a queue template entry gives to an image field, when the save can know the value
/// (feature 114). The save checks that an image has this id.
/// </summary>
/// <param name="EntryIndex">Index of the template entry.</param>
/// <param name="ParameterName">The parameter whose known value goes to the image field.</param>
/// <param name="ImageId">The image id after the substitution.</param>
/// <param name="FieldPath">The image field, for example <c>primitiveTap.detectionTarget.referenceImageId</c>.</param>
public sealed record ImageValueCandidate(
    int EntryIndex,
    string ParameterName,
    string ImageId,
    string FieldPath);

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

  /// <summary>
  /// Finds the image ids that a queue template entry gives to image fields, when the save can know
  /// the value (feature 114, FR-010). The caller checks that an image has each id.
  /// <para>
  /// The method looks at each field that the scanner marks <see cref="ParameterReference.DefeatsStaticCheck"/>
  /// in the sequence and in each command that the sequence can reach. It follows each call path:
  /// sequence step, then command, then each nested command step. For each path and each name:
  /// </para>
  /// <list type="number">
  /// <item>A non-null <c>parameterBindings</c> entry for the name at a call site on the path covers
  /// the name. The path gives no candidate for it. A literal binding is not checked, and a binding to
  /// <c>{{otherName}}</c> is not followed (a known limit; the run-time check applies).</item>
  /// <item>Else, the entry value, if the entry supplies the name.</item>
  /// <item>Else, the default of the innermost declaration layer outward along the path.</item>
  /// </list>
  /// <para>A field with a name that has no known value (for example a queue built-in) gives no candidate.</para>
  /// </summary>
  /// <param name="entry">The template entry.</param>
  /// <param name="entryIndex">Index of the entry, for the message.</param>
  /// <param name="sequence">The sequence of the entry.</param>
  /// <param name="reachableCommands">Each command that the sequence can reach.</param>
  /// <returns>The distinct candidates of the entry.</returns>
  public static IReadOnlyList<ImageValueCandidate> FindImageValueCandidates(
      QueueTemplateEntry entry,
      int entryIndex,
      CommandSequence sequence,
      IReadOnlyCollection<Command> reachableCommands) {
    ArgumentNullException.ThrowIfNull(entry);
    ArgumentNullException.ThrowIfNull(sequence);
    ArgumentNullException.ThrowIfNull(reachableCommands);

    var entryValues = new Dictionary<string, string>(StringComparer.Ordinal);
    foreach (var binding in entry.ParameterValues) {
      if (binding?.Name is null || binding.Value is null) continue;
      entryValues.TryAdd(binding.Name, binding.Value);
    }

    var commandsById = new Dictionary<string, Command>(StringComparer.OrdinalIgnoreCase);
    foreach (var command in reachableCommands) {
      if (command?.Id is not null) commandsById.TryAdd(command.Id, command);
    }

    var walk = new ImageValueWalk(entryIndex, entryValues, commandsById);
    var sequenceLayer = new[] { (IEnumerable<ParameterDeclaration>)sequence.Parameters };

    // A field in the sequence has the path "sequence" only.
    walk.AddCandidates(ParameterReferenceScanner.Scan(sequence), BindingSet.Empty, sequenceLayer);

    foreach (var step in FlattenSteps(sequence.Steps)) {
      if (string.IsNullOrWhiteSpace(step.CommandId)) continue;
      walk.WalkCommand(step.CommandId, BindingSet.Empty.With(step.ParameterBindings), sequenceLayer,
          ImmutableAncestors.Empty);
    }

    return walk.Candidates;
  }

  /// <summary>The names that a non-null binding covers on one call path.</summary>
  private sealed class BindingSet {
    private readonly HashSet<string> _names;

    private BindingSet(HashSet<string> names) => _names = names;

    public static BindingSet Empty { get; } = new(new HashSet<string>(StringComparer.Ordinal));

    public bool Covers(string name) => _names.Contains(name);

    public BindingSet With(IEnumerable<ParameterBinding>? bindings) {
      if (bindings is null) return this;
      var names = new HashSet<string>(_names, StringComparer.Ordinal);
      foreach (var binding in bindings) {
        if (binding?.Name is not null && binding.Value is not null) names.Add(binding.Name);
      }

      return names.Count == _names.Count ? this : new BindingSet(names);
    }
  }

  /// <summary>The command ids above the current command on one call path, to stop at a cycle.</summary>
  private sealed class ImmutableAncestors {
    private readonly HashSet<string> _ids;

    private ImmutableAncestors(HashSet<string> ids) => _ids = ids;

    public static ImmutableAncestors Empty { get; } = new(new HashSet<string>(StringComparer.OrdinalIgnoreCase));

    public bool Contains(string id) => _ids.Contains(id);

    public ImmutableAncestors With(string id) =>
        new(new HashSet<string>(_ids, StringComparer.OrdinalIgnoreCase) { id });
  }

  /// <summary>The state of one <see cref="FindImageValueCandidates"/> call.</summary>
  private sealed class ImageValueWalk(
      int entryIndex,
      IReadOnlyDictionary<string, string> entryValues,
      IReadOnlyDictionary<string, Command> commandsById) {
    private readonly List<ImageValueCandidate> _candidates = new();
    private readonly HashSet<ImageValueCandidate> _seen = new();

    public IReadOnlyList<ImageValueCandidate> Candidates => _candidates;

    public void WalkCommand(
        string commandId,
        BindingSet covered,
        IReadOnlyList<IEnumerable<ParameterDeclaration>> outerLayers,
        ImmutableAncestors ancestors) {
      if (ancestors.Contains(commandId)) return; // a cycle; the save of the command rejects it
      if (!commandsById.TryGetValue(commandId, out var command)) return;

      // Innermost declaration layer first, as ParameterScope looks at defaults.
      var layers = new List<IEnumerable<ParameterDeclaration>>(outerLayers.Count + 1) { command.Parameters };
      layers.AddRange(outerLayers);

      AddCandidates(CommandImageReferences(command), covered, layers);

      var inner = ancestors.With(commandId);
      foreach (var step in command.Steps) {
        if (step.Type != CommandStepType.Command || string.IsNullOrWhiteSpace(step.TargetId)) continue;
        WalkCommand(step.TargetId, covered.With(step.ParameterBindings), layers, inner);
      }
    }

    public void AddCandidates(
        IEnumerable<ParameterReference> references,
        BindingSet covered,
        IReadOnlyList<IEnumerable<ParameterDeclaration>> layers) {
      // One field can hold more than one name, so the scanner gives one reference for each name.
      // Look at each field one time.
      var fields = references
          .Where(r => r.DefeatsStaticCheck && r.SourceText is not null)
          .Select(r => (r.FieldPath, r.StepLabel, SourceText: r.SourceText!))
          .Distinct();

      foreach (var (fieldPath, _, sourceText) in fields) {
        var keys = TemplateSubstitutor.ExtractKeys(sourceText);
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        var known = true;
        foreach (var key in keys) {
          if (!TryKnownValue(key, covered, layers, out var value)) {
            known = false;
            break;
          }

          values[key] = value;
        }

        if (!known || keys.Count == 0) continue;
        var imageId = TemplateSubstitutor.Substitute(sourceText, values);
        if (string.IsNullOrWhiteSpace(imageId)) continue;

        var candidate = new ImageValueCandidate(entryIndex, keys[0], imageId.Trim(), fieldPath);
        if (_seen.Add(candidate)) _candidates.Add(candidate);
      }
    }

    private bool TryKnownValue(
        string name,
        BindingSet covered,
        IReadOnlyList<IEnumerable<ParameterDeclaration>> layers,
        out string value) {
      value = string.Empty;
      if (covered.Covers(name)) return false;
      if (ParameterNameRules.IsBuiltIn(name)
          || string.Equals(name, ParameterNameRules.IterationName, StringComparison.Ordinal)) {
        return false;
      }

      if (entryValues.TryGetValue(name, out var entryValue)) {
        value = entryValue;
        return true;
      }

      foreach (var layer in layers) {
        var declaration = layer.FirstOrDefault(d => d is not null
            && string.Equals(d.Name, name, StringComparison.Ordinal)
            && d.Default is not null);
        if (declaration is null) continue;
        value = declaration.Default!;
        return true;
      }

      return false;
    }

    /// <summary>
    /// The references of a command, less an inline image id that an overlay image key replaces at
    /// dispatch. The run does not use that inline id, so the save does not check it.
    /// </summary>
    private static IEnumerable<ParameterReference> CommandImageReferences(Command command) {
      var references = ParameterReferenceScanner.Scan(command);
      var replaced = new HashSet<(string StepLabel, string FieldPath, string SourceText)>();
      foreach (var step in command.Steps) {
        if (step.FieldTemplates is null) continue;
        var label = step.Order.ToString(System.Globalization.CultureInfo.InvariantCulture);
        AddReplaced(replaced, label, step, "primitiveTap.detectionTarget.referenceImageId",
            step.PrimitiveTap?.DetectionTarget?.ReferenceImageId);
        AddReplaced(replaced, label, step, "waitForImage.detectionTarget.referenceImageId",
            step.WaitForImage?.DetectionTarget?.ReferenceImageId);
      }

      return replaced.Count == 0
          ? references
          : references.Where(r => r.SourceText is null || !replaced.Contains((r.StepLabel, r.FieldPath, r.SourceText)));
    }

    private static void AddReplaced(
        HashSet<(string StepLabel, string FieldPath, string SourceText)> replaced,
        string label,
        CommandStep step,
        string imagePath,
        string? inlineText) {
      if (inlineText is null || step.FieldTemplates is null) return;
      if (!step.FieldTemplates.TryGetValue(imagePath, out var overlay)) return;
      if (string.Equals(overlay, inlineText, StringComparison.Ordinal)) return;
      replaced.Add((label, imagePath, inlineText));
    }
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
    foreach (var (path, value) in step.FieldTemplates) {
      if (!CommandStepFieldPaths.IsSupported(path)) {
        errors.Add(new ParameterValidationIssue(
            ParameterValidationCodes.UnknownFieldTemplatePath,
            $"Step {step.Order}: '{path}' is not a parametrizable field.",
            path));
        continue;
      }

      // Feature 114: the value of an image key must be one whole placeholder. The numeric keys get
      // no check here, so that their behavior does not change (FR-013).
      if (CommandStepFieldPaths.IsImagePath(path) && !IsWholePlaceholder(value)) {
        errors.Add(new ParameterValidationIssue(
            ParameterValidationCodes.InvalidFieldTemplateValue,
            $"Step {step.Order}: the value of '{path}' must be one whole placeholder, for example {{{{name}}}}.",
            path));
      }
    }
  }

  /// <summary>True when <paramref name="value"/> is exactly one placeholder, for example <c>{{name}}</c>.</summary>
  private static bool IsWholePlaceholder(string? value) {
    var keys = TemplateSubstitutor.ExtractKeys(value);
    return keys.Count == 1 && string.Equals(value!.Trim(), $"{{{{{keys[0]}}}}}", StringComparison.Ordinal);
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
