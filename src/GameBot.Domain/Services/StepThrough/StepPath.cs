using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using GameBot.Domain.Actions;
using GameBot.Domain.Commands;

namespace GameBot.Domain.Services.StepThrough;

/// <summary>
/// Where a step is in the step tree, and the steps around it. A path is the list of order indexes from
/// the root. An if step or a loop step has a branch marker before the index of a child:
/// <c>2</c>, <c>1/body/0</c>, <c>3/else/1</c>.
/// </summary>
/// <param name="Step">The step.</param>
/// <param name="Path">The path of the step.</param>
/// <param name="Siblings">The steps of the same list, ordered by <see cref="SequenceStep.Order"/>.</param>
/// <param name="Index">The index of the step in <paramref name="Siblings"/>.</param>
/// <param name="OwnerPath">The path of the loop or if step that holds the list. Null at the top level.</param>
/// <param name="Branch">The branch of the owner that holds the list. Null at the top level.</param>
public sealed record ResolvedStep(
  SequenceStep Step,
  string Path,
  IReadOnlyList<SequenceStep> Siblings,
  int Index,
  string? OwnerPath,
  string? Branch);

/// <summary>Builds, parses, resolves, and flattens step paths (research R2).</summary>
public static class StepPath {
  /// <summary>The branch marker for the body of a loop and the then branch of an if step.</summary>
  public const string Body = "body";

  /// <summary>The branch marker for the else branch of an if step.</summary>
  public const string Else = "else";

  /// <summary>The steps of a list in run order. The index in a path counts in this order.</summary>
  public static IReadOnlyList<SequenceStep> Ordered(IReadOnlyList<SequenceStep> steps) {
    ArgumentNullException.ThrowIfNull(steps);
    return steps.OrderBy(s => s.Order).ToList();
  }

  /// <summary>Builds the path of the child at <paramref name="index"/> in a branch of <paramref name="ownerPath"/>.</summary>
  public static string Child(string ownerPath, string branch, int index)
    => string.Create(CultureInfo.InvariantCulture, $"{ownerPath}/{branch}/{index}");

  /// <summary>Builds the path of a top-level step.</summary>
  public static string Root(int index) => index.ToString(CultureInfo.InvariantCulture);

  /// <summary>Builds the path of the sibling at <paramref name="index"/> in the list that holds <paramref name="resolved"/>.</summary>
  public static string Sibling(ResolvedStep resolved, int index) {
    ArgumentNullException.ThrowIfNull(resolved);
    return resolved.OwnerPath is null
      ? Root(index)
      : Child(resolved.OwnerPath, resolved.Branch!, index);
  }

  /// <summary>Resolves a path against the step tree. Returns false when the path is not valid for the tree.</summary>
  public static bool TryResolve(IReadOnlyList<SequenceStep> steps, string? path, out ResolvedStep? resolved) {
    ArgumentNullException.ThrowIfNull(steps);
    resolved = null;
    if (string.IsNullOrWhiteSpace(path)) return false;

    var parts = path.Split('/');
    if (parts.Length % 2 == 0) return false;

    var list = Ordered(steps);
    string? ownerPath = null;
    string? branch = null;
    var currentPath = string.Empty;
    for (var i = 0; i < parts.Length; i += 2) {
      if (!int.TryParse(parts[i], NumberStyles.None, CultureInfo.InvariantCulture, out var index)
          || index < 0
          || index >= list.Count) {
        return false;
      }

      currentPath = i == 0 ? Root(index) : Child(ownerPath!, branch!, index);
      var step = list[index];
      if (i == parts.Length - 1) {
        resolved = new ResolvedStep(step, currentPath, list, index, ownerPath, branch);
        return true;
      }

      var branchName = parts[i + 1];
      var children = BranchSteps(step, branchName);
      if (children is null) return false;
      ownerPath = currentPath;
      branch = branchName;
      list = Ordered(children);
    }

    return false;
  }

  /// <summary>
  /// The steps in a branch of a loop step or an if step. Returns null when the step has no such branch.
  /// A loop has only <see cref="Body"/>. An if step has <see cref="Body"/> (then) and <see cref="Else"/>.
  /// </summary>
  public static IReadOnlyList<SequenceStep>? BranchSteps(SequenceStep step, string branch) {
    ArgumentNullException.ThrowIfNull(step);
    if (string.Equals(branch, Body, StringComparison.Ordinal)
        && step.StepType is SequenceStepType.Loop or SequenceStepType.If) {
      return step.Body;
    }

    if (string.Equals(branch, Else, StringComparison.Ordinal) && step.StepType == SequenceStepType.If) {
      return step.ElseBody ?? Array.Empty<SequenceStep>();
    }

    return null;
  }

  /// <summary>True for a loop step or an if step. These are header rows and do not run as one unit.</summary>
  public static bool IsContainer(SequenceStep step) {
    ArgumentNullException.ThrowIfNull(step);
    return step.StepType is SequenceStepType.Loop or SequenceStepType.If;
  }

  /// <summary>Flattens the step tree into rows in display order, with depth and the selectable flag.</summary>
  public static IReadOnlyList<StepNode> Flatten(IReadOnlyList<SequenceStep> steps) {
    ArgumentNullException.ThrowIfNull(steps);
    var nodes = new List<StepNode>();
    FlattenInto(nodes, steps, null, null, 0);
    return nodes;
  }

  private static void FlattenInto(List<StepNode> nodes, IReadOnlyList<SequenceStep> steps, string? ownerPath, string? branch, int depth) {
    var list = Ordered(steps);
    for (var i = 0; i < list.Count; i++) {
      var step = list[i];
      var path = ownerPath is null ? Root(i) : Child(ownerPath, branch!, i);
      var container = IsContainer(step);
      nodes.Add(new StepNode(
        path,
        depth,
        string.IsNullOrWhiteSpace(step.StepId) ? null : step.StepId,
        TypeName(step),
        Label(step),
        container,
        !container,
        branch));
      if (!container) continue;

      FlattenInto(nodes, step.Body, path, Body, depth + 1);
      if (step.StepType == SequenceStepType.If && step.ElseBody is { Count: > 0 }) {
        FlattenInto(nodes, step.ElseBody, path, Else, depth + 1);
      }
    }
  }

  /// <summary>The type word of a step for the step list.</summary>
  public static string TypeName(SequenceStep step) {
    ArgumentNullException.ThrowIfNull(step);
    return step.StepType switch {
      SequenceStepType.Loop => "loop",
      SequenceStepType.If => "if",
      SequenceStepType.Break => "break",
      SequenceStepType.Command => "command",
      _ => IsWaitForImage(step) ? "wait-for-image" : IsCommand(step) ? "command" : "action"
    };
  }

  /// <summary>True for a step that runs a command: a command step, or an action step of the type <c>command</c>.</summary>
  private static bool IsCommand(SequenceStep step)
    => step.StepType == SequenceStepType.Command
      || step.Action is null
      || string.Equals(step.Action.Type, ActionTypes.Command, StringComparison.OrdinalIgnoreCase);

  /// <summary>The text of a step for the step list: the label of the step, or a short description.</summary>
  public static string Label(SequenceStep step) {
    ArgumentNullException.ThrowIfNull(step);
    if (!string.IsNullOrWhiteSpace(step.Label)) return step.Label!;

    return step.StepType switch {
      SequenceStepType.Loop => DescribeLoop(step),
      SequenceStepType.If => "if",
      SequenceStepType.Break => step.BreakCondition is null ? "break" : "break when",
      _ => DescribeLeaf(step)
    };
  }

  private static string DescribeLoop(SequenceStep step) => step.Loop switch {
    CountLoopConfig count => string.Create(CultureInfo.InvariantCulture, $"loop {count.Count} times"),
    WhileLoopConfig => "loop while",
    RepeatUntilLoopConfig => "loop until",
    _ => "loop"
  };

  private static string DescribeLeaf(SequenceStep step) {
    if (IsWaitForImage(step)) return "wait for image";
    if (IsCommand(step)) {
      // The name of the command is a label that the author saw when the step was saved. The id is the fallback.
      var referenceName = step.CommandReference?.CommandName;
      var name = string.IsNullOrWhiteSpace(referenceName) ? step.CommandId : referenceName;
      return string.IsNullOrWhiteSpace(name) ? "command" : $"command {name}";
    }

    if (step.Action is null) return "command";
    var type = step.Action.Type;
    if (string.Equals(type, ActionTypes.Tap, StringComparison.OrdinalIgnoreCase)
        && step.Action.Parameters.TryGetValue("x", out var x)
        && step.Action.Parameters.TryGetValue("y", out var y)) {
      return string.Create(CultureInfo.InvariantCulture, $"tap {x},{y}");
    }

    return type;
  }

  private static bool IsWaitForImage(SequenceStep step)
    => step.WaitForImage is not null
      || string.Equals(step.Action?.Type, ActionTypes.WaitForImage, StringComparison.OrdinalIgnoreCase);
}
