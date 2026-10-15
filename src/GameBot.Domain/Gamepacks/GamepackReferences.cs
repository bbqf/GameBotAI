using GameBot.Domain.Commands;

namespace GameBot.Domain.Gamepacks;

/// <summary>
/// Finds the command and image references of sequences and commands, and rewrites command references (feature 143,
/// FR-020). A sequence step names a command by its id. A command step of type Command names a command by its id.
/// </summary>
public static class GamepackReferences {
  /// <summary>Gets the command ids that a sequence names.</summary>
  /// <param name="sequence">The sequence.</param>
  public static IEnumerable<string> CommandIds(CommandSequence sequence) {
    ArgumentNullException.ThrowIfNull(sequence);
    return Flatten(sequence.Steps).SelectMany(StepCommandIds);
  }

  /// <summary>Gets the image ids that a sequence names.</summary>
  /// <param name="sequence">The sequence.</param>
  public static IEnumerable<string> ImageIds(CommandSequence sequence) {
    ArgumentNullException.ThrowIfNull(sequence);
    return Flatten(sequence.Steps).SelectMany(StepImageIds);
  }

  /// <summary>Gets the command ids that a command names.</summary>
  /// <param name="command">The command.</param>
  public static IEnumerable<string> CommandIds(Command command) {
    ArgumentNullException.ThrowIfNull(command);
    return command.Steps
      .Where(s => s.Type == CommandStepType.Command && !string.IsNullOrWhiteSpace(s.TargetId))
      .Select(s => s.TargetId);
  }

  /// <summary>Gets the image ids that a command names.</summary>
  /// <param name="command">The command.</param>
  public static IEnumerable<string> ImageIds(Command command) {
    ArgumentNullException.ThrowIfNull(command);
    var ids = new List<string>();
    AddIfSet(ids, command.Detection?.ReferenceImageId);
    foreach (var step in command.Steps) {
      AddIfSet(ids, step.PrimitiveTap?.DetectionTarget?.ReferenceImageId);
      AddIfSet(ids, step.WaitForImage?.DetectionTarget?.ReferenceImageId);
      AddIfSet(ids, step.EnsureGameRunning?.ReadinessImage?.ReferenceImageId);
    }

    return ids;
  }

  /// <summary>Replaces command ids in a sequence. The map gives the new id, or null to keep the id.</summary>
  /// <param name="sequence">The sequence. It changes in place.</param>
  /// <param name="map">The id map.</param>
  public static void RewriteCommandIds(CommandSequence sequence, Func<string, string?> map) {
    ArgumentNullException.ThrowIfNull(sequence);
    ArgumentNullException.ThrowIfNull(map);
    foreach (var step in Flatten(sequence.Steps)) {
      if (!string.IsNullOrWhiteSpace(step.CommandId)) {
        step.CommandId = map(step.CommandId) ?? step.CommandId;
      }

      if (step.CommandReference is not null && !string.IsNullOrWhiteSpace(step.CommandReference.CommandId)) {
        step.CommandReference.CommandId = map(step.CommandReference.CommandId) ?? step.CommandReference.CommandId;
      }
    }
  }

  /// <summary>Replaces command ids in the steps of a command. The map gives the new id, or null to keep the id.</summary>
  /// <param name="command">The command. It changes in place.</param>
  /// <param name="map">The id map.</param>
  public static void RewriteCommandIds(Command command, Func<string, string?> map) {
    ArgumentNullException.ThrowIfNull(command);
    ArgumentNullException.ThrowIfNull(map);
    for (var i = 0; i < command.Steps.Count; i++) {
      var step = command.Steps[i];
      if (step.Type != CommandStepType.Command || string.IsNullOrWhiteSpace(step.TargetId)) {
        continue;
      }

      var mapped = map(step.TargetId);
      if (mapped is null || string.Equals(mapped, step.TargetId, StringComparison.Ordinal)) {
        continue;
      }

      command.Steps[i] = new CommandStep {
        Type = step.Type,
        TargetId = mapped,
        PrimitiveTap = step.PrimitiveTap,
        WaitForImage = step.WaitForImage,
        KeyInput = step.KeyInput,
        Swipe = step.Swipe,
        EnsureEmulatorRunning = step.EnsureEmulatorRunning,
        EnsureGameRunning = step.EnsureGameRunning,
        Order = step.Order,
        FieldTemplates = step.FieldTemplates,
        ParameterBindings = step.ParameterBindings
      };
    }
  }

  private static IEnumerable<SequenceStep> Flatten(IEnumerable<SequenceStep> steps) {
    foreach (var step in steps) {
      yield return step;
      foreach (var child in Flatten(step.Body)) {
        yield return child;
      }

      if (step.ElseBody is not null) {
        foreach (var child in Flatten(step.ElseBody)) {
          yield return child;
        }
      }
    }
  }

  private static IEnumerable<string> StepCommandIds(SequenceStep step) {
    if (!string.IsNullOrWhiteSpace(step.CommandId)) {
      yield return step.CommandId;
    }
  }

  private static IEnumerable<string> StepImageIds(SequenceStep step) {
    var ids = new List<string>();
    AddIfSet(ids, step.Gate?.TargetId);
    AddIfSet(ids, step.WaitForImage?.DetectionTarget?.ReferenceImageId);
    AddIfSet(ids, step.ConditionExpression?.ImageId);
    AddConditionImages(ids, step.Condition);
    AddConditionImages(ids, step.BreakCondition);
    AddConditionImages(ids, step.If?.Condition);
    switch (step.Loop) {
      case WhileLoopConfig w:
        AddConditionImages(ids, w.Condition);
        break;
      case RepeatUntilLoopConfig r:
        AddConditionImages(ids, r.Condition);
        break;
    }

    return ids;
  }

  private static void AddConditionImages(List<string> ids, SequenceStepCondition? condition) {
    switch (condition) {
      case ImageVisibleStepCondition leaf:
        AddIfSet(ids, leaf.ImageId);
        break;
      case CompositeStepCondition composite:
        foreach (var child in composite.Children) {
          AddConditionImages(ids, child);
        }

        break;
    }
  }

  private static void AddIfSet(List<string> ids, string? id) {
    if (!string.IsNullOrWhiteSpace(id)) {
      ids.Add(id);
    }
  }
}
