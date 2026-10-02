using System;
using System.Collections.Generic;

namespace GameBot.Domain.Services.StepThrough;

/// <summary>The kind of an open loop or an open if branch in the frame stack of a step-through.</summary>
public enum FrameKind {
  /// <summary>A loop that runs its body a fixed number of times.</summary>
  LoopCount,

  /// <summary>A loop that checks its condition before each iteration.</summary>
  LoopWhile,

  /// <summary>A loop that checks its exit condition after each iteration.</summary>
  LoopRepeatUntil,

  /// <summary>An if step that runs its then branch.</summary>
  IfThen,

  /// <summary>An if step that runs its else branch.</summary>
  IfElse
}

/// <summary>
/// One open loop or one open if branch. A frame lives from the moment the cursor enters the body of
/// the owner until the body ends.
/// </summary>
/// <param name="OwnerPath">The path of the loop step or the if step.</param>
/// <param name="Kind">The kind of the frame.</param>
/// <param name="Iteration">The 1-based iteration number. Loops only. It is 1 for an if frame.</param>
/// <param name="Limit">The count of a count loop. It is null for the other kinds.</param>
public sealed record Frame(string OwnerPath, FrameKind Kind, int Iteration, int? Limit) {
  /// <summary>True for the three loop kinds.</summary>
  public bool IsLoop => Kind is FrameKind.LoopCount or FrameKind.LoopWhile or FrameKind.LoopRepeatUntil;
}

/// <summary>The kind of a history entry.</summary>
public enum HistoryKind {
  /// <summary>One step ran.</summary>
  Step,

  /// <summary>The cursor entered a loop or an if step. The entry holds the decision.</summary>
  Enter,

  /// <summary>A loop ended.</summary>
  Exit
}

/// <summary>One row of the step list. A loop or an if step is a header row and is not selectable.</summary>
/// <param name="Path">The path of the step, for example <c>1/body/0</c>.</param>
/// <param name="Depth">0 for a top-level step.</param>
/// <param name="StepId">The step id from the sequence. It can be empty.</param>
/// <param name="Type">One of <c>action</c>, <c>command</c>, <c>loop</c>, <c>if</c>, <c>break</c>, <c>wait-for-image</c>.</param>
/// <param name="Label">The text for the list.</param>
/// <param name="Container">True for a loop or an if step.</param>
/// <param name="Selectable">False for a header row.</param>
/// <param name="Branch">The branch of the parent that holds this step: <c>body</c>, <c>else</c>, or null at the top level.</param>
public sealed record StepNode(
  string Path,
  int Depth,
  string? StepId,
  string Type,
  string Label,
  bool Container,
  bool Selectable,
  string? Branch = null);

/// <summary>One entry in the history of a step-through.</summary>
/// <param name="Seq">Increases by 1 for each entry.</param>
/// <param name="Path">The path of the step.</param>
/// <param name="StepId">The step id from the sequence.</param>
/// <param name="Kind">The kind of the entry.</param>
/// <param name="Iteration">The iteration number for a step inside a loop.</param>
/// <param name="Status">One of <c>Succeeded</c>, <c>Failed</c>, <c>Skipped</c>, <c>Cancelled</c>.</param>
/// <param name="Outcome">For example <c>executed</c>, <c>not_executed</c>, <c>break</c>, <c>no_break</c>.</param>
/// <param name="Message">The result text.</param>
/// <param name="Effects">The previewed effects, for example <c>would reschedule at 14:30</c>.</param>
/// <param name="Notes">Notes, for example <c>lastRun is not evaluated</c>.</param>
/// <param name="StartedAt">The start time.</param>
/// <param name="DurationMs">The duration in milliseconds.</param>
/// <param name="ExecutionLogId">The id of the execution log entry, when one exists.</param>
public sealed record HistoryEntry(
  int Seq,
  string Path,
  string? StepId,
  HistoryKind Kind,
  int? Iteration,
  string Status,
  string? Outcome,
  string? Message,
  IReadOnlyList<string> Effects,
  IReadOnlyList<string> Notes,
  DateTimeOffset StartedAt,
  int DurationMs,
  string? ExecutionLogId = null);
