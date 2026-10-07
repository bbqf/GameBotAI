using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GameBot.Domain.Commands;
using GameBot.Domain.Parameters;

namespace GameBot.Domain.Services.StepThrough;

/// <summary>The result of <see cref="SequenceStepper.Select"/>.</summary>
public enum StepSelectResult {
  /// <summary>The step is the next step now.</summary>
  Ok,

  /// <summary>No step has this path.</summary>
  UnknownStep,

  /// <summary>The path is a loop step or an if step. These are header rows and do not run as one unit.</summary>
  NotSelectable
}

/// <summary>
/// Runs a saved sequence one step at a time (feature 127). The stepper keeps an explicit cursor over the
/// step tree. It runs one step that is not a container for each call, with the leaf code of
/// <see cref="SequenceRunner"/>. It decides the next step with the same rules as a real run: guards,
/// count loops, while loops, repeat-until loops, <c>maxIterations</c>, if branches, and break.
/// <para>
/// The stepper evaluates a loop or an if step when the author runs the next step, not before. The
/// cursor can therefore be on a header row. This is the moment when a real run would evaluate it.
/// A parity test compares the order of the stepper with the order of the real runner (SC-002).
/// </para>
/// </summary>
public sealed class SequenceStepper {
  /// <summary>The note for a condition that reads the queue run history. A step-through has no queue run.</summary>
  public const string LastRunNote = "lastRun is not evaluated";

  /// <summary>The note for a step that a real run would end the sequence at.</summary>
  public const string SequenceEndNote = "sequence would end here";

  /// <summary>The note for a break step with no loop around it.</summary>
  public const string NoLoopNote = "no loop is active";

  // A safety bound for the loop checks that one call can make. A while loop with no body and a true
  // condition would otherwise run to the loop ceiling in one call. The ceiling stops it, and this bound
  // is a second guard for a very high ceiling.
  private const int MaxTransitionsPerCall = 10_000;

  private readonly SequenceRunner _runner;

  /// <summary>Creates a stepper that uses the leaf code of <paramref name="runner"/>.</summary>
  public SequenceStepper(SequenceRunner runner) {
    ArgumentNullException.ThrowIfNull(runner);
    _runner = runner;
  }

  /// <summary>
  /// Makes the step at <paramref name="path"/> the next step (FR-007). It opens a frame for each loop and
  /// each if branch around the step, at iteration 1. It does not evaluate any condition: the author chose
  /// the step. Does not change the state when the result is not <see cref="StepSelectResult.Ok"/>.
  /// </summary>
  public static StepSelectResult Select(CommandSequence sequence, StepperState state, string? path) {
    ArgumentNullException.ThrowIfNull(sequence);
    ArgumentNullException.ThrowIfNull(state);
    if (!StepPath.TryResolve(sequence.Steps, path, out var resolved) || resolved is null) {
      return StepSelectResult.UnknownStep;
    }

    if (StepPath.IsContainer(resolved.Step)) return StepSelectResult.NotSelectable;

    var frames = new List<Frame>();
    var parts = resolved.Path.Split('/');
    for (var i = 0; i + 2 < parts.Length; i += 2) {
      var ownerPath = string.Join('/', parts.Take(i + 1));
      if (!StepPath.TryResolve(sequence.Steps, ownerPath, out var owner) || owner is null) {
        return StepSelectResult.UnknownStep;
      }

      frames.Add(OpenFrame(owner, parts[i + 1]));
    }

    state.Frames.Clear();
    foreach (var frame in frames) state.Frames.Add(frame);
    state.Cursor = resolved.Path;
    return StepSelectResult.Ok;
  }

  /// <summary>
  /// Runs the next step. First, it evaluates the loops and the if steps on the way. Each decision is one
  /// history entry. Then it runs one step that is not a container. Then it moves the cursor to the step
  /// that a real run would run next. It returns the entries that it added. A call with a null cursor
  /// does nothing. A cancelled token throws <see cref="OperationCanceledException"/>. The cursor then
  /// stays on the step that did not finish.
  /// </summary>
  public async Task<IReadOnlyList<HistoryEntry>> RunNextAsync(
      CommandSequence sequence,
      StepperState state,
      StepperDependencies dependencies,
      CancellationToken ct = default) {
    ArgumentNullException.ThrowIfNull(sequence);
    ArgumentNullException.ThrowIfNull(state);
    ArgumentNullException.ThrowIfNull(dependencies);

    var produced = new List<HistoryEntry>();
    var baseScope = BuildBaseScope(sequence, state);
    for (var transitions = 0; state.Cursor is not null; transitions++) {
      ct.ThrowIfCancellationRequested();
      if (transitions >= MaxTransitionsPerCall) {
        produced.Add(Record(
          state,
          state.Cursor,
          null,
          HistoryKind.Enter,
          null,
          "Failed",
          "failed",
          string.Create(CultureInfo.InvariantCulture, $"The step-through stopped after {MaxTransitionsPerCall} loop checks with no step to run. Select a step to go on."),
          DateTimeOffset.UtcNow,
          TimeSpan.Zero));
        break;
      }

      if (!StepPath.TryResolve(sequence.Steps, state.Cursor, out var resolved) || resolved is null) {
        state.Cursor = null;
        break;
      }

      var step = resolved.Step;
      if (step.StepType == SequenceStepType.Loop) {
        var top = state.Frames.Count > 0 ? state.Frames[^1] : null;
        if (top is not null && top.IsLoop && string.Equals(top.OwnerPath, resolved.Path, StringComparison.Ordinal)) {
          await ContinueLoopAsync(sequence, state, resolved, dependencies, baseScope, produced, ct).ConfigureAwait(false);
        }
        else {
          await EnterLoopAsync(sequence, state, resolved, dependencies, baseScope, produced, ct).ConfigureAwait(false);
        }

        continue;
      }

      if (step.StepType == SequenceStepType.If) {
        await EnterIfAsync(sequence, state, resolved, dependencies, baseScope, produced, ct).ConfigureAwait(false);
        continue;
      }

      await RunLeafAsync(sequence, state, resolved, dependencies, baseScope, produced, ct).ConfigureAwait(false);
      break;
    }

    return produced;
  }

  // ── Scope ─────────────────────────────────────────────────────────────────────────────────────

  private static ParameterScope BuildBaseScope(CommandSequence sequence, StepperState state) {
    var bindings = state.ParameterValues.Select(pair => new ParameterBinding { Name = pair.Key, Value = pair.Value });
    return ParameterScope.Empty
      .Child(ParameterScopeLayers.Entry, bindings, null)
      .Child(ParameterScopeLayers.Sequence, null, sequence.Parameters);
  }

  /// <summary>The scope for a step that sits inside the first <paramref name="count"/> frames.</summary>
  private static ParameterScope ScopeFor(ParameterScope baseScope, Collection<Frame> frames, int count) {
    for (var i = count - 1; i >= 0; i--) {
      if (frames[i].IsLoop) return baseScope.WithIteration(frames[i].Iteration);
    }

    return baseScope;
  }

  private static int? InnermostIteration(Collection<Frame> frames) {
    for (var i = frames.Count - 1; i >= 0; i--) {
      if (frames[i].IsLoop) return frames[i].Iteration;
    }

    return null;
  }

  private static Frame OpenFrame(ResolvedStep owner, string branch) {
    var step = owner.Step;
    if (step.StepType == SequenceStepType.If) {
      return new Frame(
        owner.Path,
        string.Equals(branch, StepPath.Else, StringComparison.Ordinal) ? FrameKind.IfElse : FrameKind.IfThen,
        1,
        null);
    }

    return step.Loop switch {
      CountLoopConfig count => new Frame(owner.Path, FrameKind.LoopCount, 1, count.Count),
      RepeatUntilLoopConfig => new Frame(owner.Path, FrameKind.LoopRepeatUntil, 1, null),
      _ => new Frame(owner.Path, FrameKind.LoopWhile, 1, null)
    };
  }

  // ── History ───────────────────────────────────────────────────────────────────────────────────

  private static HistoryEntry Record(
      StepperState state,
      string path,
      string? stepId,
      HistoryKind kind,
      int? iteration,
      string status,
      string? outcome,
      string? message,
      DateTimeOffset startedAt,
      TimeSpan elapsed,
      IReadOnlyList<string>? effects = null,
      IReadOnlyList<string>? notes = null)
    => state.Append(new HistoryEntry(
      0,
      path,
      string.IsNullOrWhiteSpace(stepId) ? null : stepId,
      kind,
      iteration,
      status,
      outcome,
      message,
      effects ?? Array.Empty<string>(),
      notes ?? Array.Empty<string>(),
      startedAt,
      (int)elapsed.TotalMilliseconds));

  private static bool HasLastRun(SequenceStepCondition? condition) {
    return condition switch {
      null => false,
      LastRunStepCondition => true,
      CompositeStepCondition composite => composite.Children.Any(HasLastRun),
      _ => false
    };
  }

  private static string[] ConditionNotes(params SequenceStepCondition?[] conditions)
    => conditions.Any(HasLastRun) ? new[] { LastRunNote } : Array.Empty<string>();

  // ── Moving the cursor ─────────────────────────────────────────────────────────────────────────

  /// <summary>
  /// Moves the cursor to the step after <paramref name="path"/> in the normal order: the next step in
  /// the same list; else the next iteration check of the loop around it; else the step after the
  /// owner. A step at the end of the top-level list completes the sequence.
  /// </summary>
  private static void AdvanceFrom(CommandSequence sequence, StepperState state, string path) {
    var current = path;
    while (true) {
      if (!StepPath.TryResolve(sequence.Steps, current, out var resolved) || resolved is null) {
        state.Cursor = null;
        return;
      }

      if (resolved.Index + 1 < resolved.Siblings.Count) {
        state.Cursor = StepPath.Sibling(resolved, resolved.Index + 1);
        return;
      }

      if (resolved.OwnerPath is null) {
        state.Cursor = null;
        return;
      }

      var owner = resolved.OwnerPath;
      var top = state.Frames.Count > 0 ? state.Frames[^1] : null;
      if (top is not null && string.Equals(top.OwnerPath, owner, StringComparison.Ordinal)) {
        if (top.IsLoop) {
          state.Cursor = owner;
          return;
        }

        state.Frames.RemoveAt(state.Frames.Count - 1);
      }

      current = owner;
    }
  }

  private static void StartBody(StepperState state, ResolvedStep owner, string branch) {
    var children = StepPath.BranchSteps(owner.Step, branch);
    state.Cursor = children is { Count: > 0 } ? StepPath.Child(owner.Path, branch, 0) : owner.Path;
  }

  // ── Leaf steps ────────────────────────────────────────────────────────────────────────────────

  private async Task RunLeafAsync(
      CommandSequence sequence,
      StepperState state,
      ResolvedStep resolved,
      StepperDependencies dependencies,
      ParameterScope baseScope,
      List<HistoryEntry> produced,
      CancellationToken ct) {
    var step = resolved.Step;
    var scope = ScopeFor(baseScope, state.Frames, state.Frames.Count);
    var iteration = InnermostIteration(state.Frames);
    var started = DateTimeOffset.UtcNow;
    var stopwatch = Stopwatch.StartNew();
    var notes = new List<string>(ConditionNotes(step.Condition, step.BreakCondition));

    if (step.StepType == SequenceStepType.Break) {
      await RunBreakAsync(sequence, state, resolved, dependencies, scope, iteration, started, stopwatch, notes, produced, ct).ConfigureAwait(false);
      return;
    }

    var result = SequenceExecutionResult.Start(sequence.Id);
    var previewed = new List<string>();
    var earlyStop = await _runner.ExecuteLeafAsync(
      step,
      dependencies.ExecuteCommandAsync,
      WrapCommandDispatcher(dependencies, previewed),
      dependencies.GateEvaluator,
      dependencies.ConditionEvaluator,
      state.Outcomes,
      result,
      sequence.Id,
      WrapActionDispatcher(dependencies),
      scope,
      ct).ConfigureAwait(false);

    // The leaf code records a cancelled step as a failed step, as a real run does. A step-through reports
    // it as cancelled instead, so it throws here and does not move the cursor.
    ct.ThrowIfCancellationRequested();

    var stepResult = result.Steps.Count > 0 ? result.Steps[^1] : null;
    var status = stepResult?.Status ?? (earlyStop ? "Failed" : "Succeeded");
    var message = stepResult?.Message;
    if (stepResult is null && earlyStop) {
      message = step.Gate is not null
        ? "The step failed: its gate did not open before the timeout."
        : "The step failed.";
    }

    var effects = new List<string>(previewed);
    if (string.Equals(stepResult?.ActionOutcome, PreviewOutcome, StringComparison.Ordinal)
        && !string.IsNullOrWhiteSpace(stepResult?.Message)) {
      effects.Add(stepResult!.Message!);
    }

    if (earlyStop) notes.Add(SequenceEndNote);

    produced.Add(Record(
      state,
      resolved.Path,
      step.StepId,
      HistoryKind.Step,
      iteration,
      status,
      stepResult?.ActionOutcome ?? (earlyStop ? "failed" : null),
      message,
      started,
      stopwatch.Elapsed,
      effects,
      notes));
    AdvanceFrom(sequence, state, resolved.Path);
  }

  /// <summary>The action outcome of a service-level action that a step-through previewed.</summary>
  public const string PreviewOutcome = "previewed";

  private static Func<string, ParameterScope, Task<CommandDispatchOutcome>>? WrapCommandDispatcher(
      StepperDependencies dependencies,
      List<string> previewed) {
    if (dependencies.CommandDispatcher is not { } inner) return null;
    return async (commandId, scope) => {
      var outcome = await inner(commandId, scope).ConfigureAwait(false);
      if (outcome.PreviewedEffects is { Count: > 0 } effects) previewed.AddRange(effects);
      return outcome;
    };
  }

  private static Func<SequenceActionPayload, CancellationToken, Task<ActionDispatchResult>>? WrapActionDispatcher(
      StepperDependencies dependencies) {
    if (dependencies.ActionDispatcher is null && dependencies.PreviewServiceAction is null) return null;
    return (action, token) => {
      // An action with an outside effect never reaches the real dispatcher (FR-015).
      // A restart step stops the game on the device, so it is previewed too (feature 129, FR-012).
      if (SequenceRunner.IsServiceLevelAction(action) || SequenceRunner.IsForceRestartAction(action)) {
        return dependencies.PreviewServiceAction is { } preview
          ? preview(action, token)
          : Task.FromResult(new ActionDispatchResult(PreviewOutcome, $"would run '{action.Type}'"));
      }

      return dependencies.ActionDispatcher is { } real
        ? real(action, token)
        : Task.FromResult(new ActionDispatchResult("failed", $"No dispatcher is set for the action '{action.Type}'."));
    };
  }

  private static async Task RunBreakAsync(
      CommandSequence sequence,
      StepperState state,
      ResolvedStep resolved,
      StepperDependencies dependencies,
      ParameterScope scope,
      int? iteration,
      DateTimeOffset started,
      Stopwatch stopwatch,
      List<string> notes,
      List<HistoryEntry> produced,
      CancellationToken ct) {
    var step = resolved.Step;
    var key = string.IsNullOrWhiteSpace(step.StepId) ? "break" : step.StepId;
    bool fired;
    string message;

    if (step.BreakCondition is null) {
      fired = true;
      message = "Unconditional break triggered";
    }
    else {
      var description = SequenceRunner.DescribeBreakCondition(step.BreakCondition);
      try {
        // An unresolved name or a failed evaluation is a neutral "No break", as in a real run (feature 066).
        var resolvedCondition = SequenceRunner.ResolveCondition(step.BreakCondition, scope, "breakCondition", key);
        description = SequenceRunner.DescribeBreakCondition(resolvedCondition);
        fired = await SequenceRunner.EvaluateResolvedConditionAsync(
          resolvedCondition, dependencies.ConditionEvaluator, state.Outcomes, ct).ConfigureAwait(false);
        message = fired
          ? $"Break triggered: {description.Detail} evaluated to true"
          : $"No break: {description.Detail} evaluated to false";
      }
      catch (Exception ex) when (ex is not OperationCanceledException) {
        fired = false;
        message = $"No break: {description.Detail} could not be evaluated ({ex.Message})";
      }
    }

    var outcome = fired ? BreakOutcomes.Break : BreakOutcomes.NoBreak;
    state.Outcomes[key] = outcome;

    var loopIndex = -1;
    if (fired) {
      for (var i = state.Frames.Count - 1; i >= 0; i--) {
        if (state.Frames[i].IsLoop) {
          loopIndex = i;
          break;
        }
      }

      if (loopIndex < 0) notes.Add(NoLoopNote);
    }

    produced.Add(Record(
      state,
      resolved.Path,
      step.StepId,
      HistoryKind.Step,
      iteration,
      "Succeeded",
      outcome,
      message,
      started,
      stopwatch.Elapsed,
      null,
      notes));

    if (!fired || loopIndex < 0) {
      AdvanceFrom(sequence, state, resolved.Path);
      return;
    }

    // The break ends the nearest loop. It closes the frames inside the loop and the frame of the loop.
    var loopFrame = state.Frames[loopIndex];
    for (var i = state.Frames.Count - 1; i >= loopIndex; i--) state.Frames.RemoveAt(i);
    var loopKey = LoopKeyAt(sequence, loopFrame.OwnerPath);
    state.Outcomes[loopKey] = "success";
    produced.Add(Record(
      state,
      loopFrame.OwnerPath,
      null,
      HistoryKind.Exit,
      loopFrame.Iteration,
      "Succeeded",
      BreakOutcomes.Break,
      $"Loop '{loopKey}' ended by break after {loopFrame.Iteration} iteration(s).",
      DateTimeOffset.UtcNow,
      TimeSpan.Zero));
    AdvanceFrom(sequence, state, loopFrame.OwnerPath);
  }

  private static string LoopKeyAt(CommandSequence sequence, string path)
    => StepPath.TryResolve(sequence.Steps, path, out var owner) && owner is not null
      ? SequenceRunner.LoopStepKey(owner.Step)
      : path;

  // ── Loop steps ────────────────────────────────────────────────────────────────────────────────

  private static async Task EnterLoopAsync(
      CommandSequence sequence,
      StepperState state,
      ResolvedStep resolved,
      StepperDependencies dependencies,
      ParameterScope baseScope,
      List<HistoryEntry> produced,
      CancellationToken ct) {
    var step = resolved.Step;
    var path = resolved.Path;
    var key = SequenceRunner.LoopStepKey(step);
    var started = DateTimeOffset.UtcNow;
    var stopwatch = Stopwatch.StartNew();
    var scope = ScopeFor(baseScope, state.Frames, state.Frames.Count);
    var loopCondition = step.Loop switch {
      WhileLoopConfig w => w.Condition,
      RepeatUntilLoopConfig r => r.Condition,
      _ => null
    };
    var notes = ConditionNotes(step.Condition, loopCondition);

    void Enter(string status, string outcome, string? message)
      => produced.Add(Record(state, path, step.StepId, HistoryKind.Enter, null, status, outcome, message, started, stopwatch.Elapsed, null, notes));

    if (step.Condition is not null) {
      string? guardDetail = null;
      var guard = await SequenceRunner.EvaluateStepGuardAsync(
        step.Condition,
        key,
        scope,
        dependencies.ConditionEvaluator,
        state.Outcomes,
        SequenceExecutionResult.Start(sequence.Id),
        (_, _, _, detail) => guardDetail = detail,
        ct).ConfigureAwait(false);
      if (guard == SequenceRunner.StepGuardDecision.Skip) {
        Enter("Skipped", "skipped", guardDetail is null
          ? $"Loop '{key}' skipped: its condition is false."
          : $"Loop '{key}' skipped: its condition is false; {guardDetail}.");
        AdvanceFrom(sequence, state, path);
        return;
      }

      if (guard == SequenceRunner.StepGuardDecision.Fail) {
        Enter("Failed", "failed", guardDetail);
        AdvanceFrom(sequence, state, path);
        return;
      }
    }

    switch (step.Loop) {
      case CountLoopConfig count:
        if (count.Count <= 0) {
          state.Outcomes[key] = "success";
          Enter("Succeeded", "none", $"Loop '{key}' has no iterations.");
          AdvanceFrom(sequence, state, path);
          return;
        }

        state.Frames.Add(new Frame(path, FrameKind.LoopCount, 1, count.Count));
        Enter("Succeeded", "enter", string.Create(CultureInfo.InvariantCulture, $"Loop '{key}' starts. Iteration 1 of {count.Count}."));
        StartBody(state, resolved, StepPath.Body);
        return;

      case WhileLoopConfig whileLoop:
        bool condition;
        try {
          condition = await SequenceRunner.EvaluateLoopConditionAsync(
            whileLoop.Condition, scope.WithIteration(1), "loop.condition", key,
            dependencies.ConditionEvaluator, state.Outcomes, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException) {
          state.Outcomes[key] = "failed";
          Enter("Failed", "failed", $"Loop '{key}' condition evaluation failed: {ex.Message}");
          AdvanceFrom(sequence, state, path);
          return;
        }

        if (!condition) {
          state.Outcomes[key] = "skipped";
          Enter("Skipped", "skipped", $"Loop '{key}' skipped: its condition is false.");
          AdvanceFrom(sequence, state, path);
          return;
        }

        state.Frames.Add(new Frame(path, FrameKind.LoopWhile, 1, null));
        Enter("Succeeded", "enter", $"Loop '{key}' starts. Iteration 1.");
        StartBody(state, resolved, StepPath.Body);
        return;

      case RepeatUntilLoopConfig:
        state.Frames.Add(new Frame(path, FrameKind.LoopRepeatUntil, 1, null));
        Enter("Succeeded", "enter", $"Loop '{key}' starts. Iteration 1.");
        StartBody(state, resolved, StepPath.Body);
        return;

      default:
        state.Outcomes[key] = "failed";
        Enter("Failed", "failed", $"Loop step '{key}' has missing or unknown loop configuration.");
        AdvanceFrom(sequence, state, path);
        return;
    }
  }

  private async Task ContinueLoopAsync(
      CommandSequence sequence,
      StepperState state,
      ResolvedStep resolved,
      StepperDependencies dependencies,
      ParameterScope baseScope,
      List<HistoryEntry> produced,
      CancellationToken ct) {
    var step = resolved.Step;
    var path = resolved.Path;
    var key = SequenceRunner.LoopStepKey(step);
    var started = DateTimeOffset.UtcNow;
    var stopwatch = Stopwatch.StartNew();
    var frame = state.Frames[^1];
    var enclosing = ScopeFor(baseScope, state.Frames, state.Frames.Count - 1);
    var maxIterations = step.Loop?.MaxIterations ?? _runner.LoopMaxIterations;

    void Exit(string status, string outcome, string message, string stepOutcome) {
      state.Frames.RemoveAt(state.Frames.Count - 1);
      state.Outcomes[key] = stepOutcome;
      produced.Add(Record(state, path, step.StepId, HistoryKind.Exit, frame.Iteration, status, outcome, message, started, stopwatch.Elapsed));
      AdvanceFrom(sequence, state, path);
    }

    void Next(int iteration) {
      state.Frames[^1] = frame with { Iteration = iteration };
      StartBody(state, resolved, StepPath.Body);
    }

    void Ceiling(string detail, bool exitOnMax) {
      if (exitOnMax) {
        Exit("Succeeded", "exhausted", $"Loop '{key}' gave up after {maxIterations} iterations with {detail}.", "not_executed");
      }
      else {
        Exit("Failed", "failed", $"Loop '{key}' exceeded maximum iterations ({maxIterations}).", "failed");
      }
    }

    switch (step.Loop) {
      case CountLoopConfig count:
        if (frame.Iteration < (frame.Limit ?? count.Count)) Next(frame.Iteration + 1);
        else Exit("Succeeded", "completed", $"Loop '{key}' ended after {frame.Iteration} iteration(s).", "success");
        return;

      case WhileLoopConfig whileLoop: {
        var next = frame.Iteration + 1;
        bool condition;
        try {
          condition = await SequenceRunner.EvaluateLoopConditionAsync(
            whileLoop.Condition, enclosing.WithIteration(next), "loop.condition", key,
            dependencies.ConditionEvaluator, state.Outcomes, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException) {
          Exit("Failed", "failed", $"Loop '{key}' condition evaluation failed: {ex.Message}", "failed");
          return;
        }

        if (!condition) Exit("Succeeded", "condition_false", $"Loop '{key}' ended after {frame.Iteration} iteration(s): its condition is false.", "success");
        else if (next > maxIterations) Ceiling("its condition still true", whileLoop.ExitOnMaxIterations);
        else Next(next);
        return;
      }

      case RepeatUntilLoopConfig repeat: {
        if (frame.Iteration >= maxIterations) {
          Ceiling("its exit condition still false", repeat.ExitOnMaxIterations);
          return;
        }

        bool exit;
        try {
          exit = await SequenceRunner.EvaluateLoopConditionAsync(
            repeat.Condition, enclosing.WithIteration(frame.Iteration), "loop.condition", key,
            dependencies.ConditionEvaluator, state.Outcomes, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException) {
          Exit("Failed", "failed", $"Loop '{key}' exit condition evaluation failed: {ex.Message}", "failed");
          return;
        }

        if (exit) Exit("Succeeded", "condition_true", $"Loop '{key}' ended after {frame.Iteration} iteration(s): its exit condition is true.", "success");
        else Next(frame.Iteration + 1);
        return;
      }

      default:
        Exit("Failed", "failed", $"Loop step '{key}' has missing or unknown loop configuration.", "failed");
        return;
    }
  }

  // ── If steps ──────────────────────────────────────────────────────────────────────────────────

  private static async Task EnterIfAsync(
      CommandSequence sequence,
      StepperState state,
      ResolvedStep resolved,
      StepperDependencies dependencies,
      ParameterScope baseScope,
      List<HistoryEntry> produced,
      CancellationToken ct) {
    var step = resolved.Step;
    var path = resolved.Path;
    var key = string.IsNullOrWhiteSpace(step.StepId) ? $"if@{step.Order}" : step.StepId;
    var started = DateTimeOffset.UtcNow;
    var stopwatch = Stopwatch.StartNew();
    var scope = ScopeFor(baseScope, state.Frames, state.Frames.Count);
    var notes = ConditionNotes(step.If?.Condition);

    void Enter(string status, string outcome, string message)
      => produced.Add(Record(state, path, step.StepId, HistoryKind.Enter, null, status, outcome, message, started, stopwatch.Elapsed, null, notes));

    if (step.If is null) {
      state.Outcomes[key] = "failed";
      Enter("Failed", "failed", $"If step '{key}' has missing if configuration.");
      AdvanceFrom(sequence, state, path);
      return;
    }

    var description = SequenceRunner.DescribeBreakCondition(step.If.Condition);
    bool condition;
    try {
      var resolvedCondition = SequenceRunner.ResolveCondition(step.If.Condition, scope, "if.condition", key);
      description = SequenceRunner.DescribeBreakCondition(resolvedCondition);
      condition = await SequenceRunner.EvaluateResolvedConditionAsync(
        resolvedCondition, dependencies.ConditionEvaluator, state.Outcomes, ct).ConfigureAwait(false);
    }
    catch (Exception ex) when (ex is not OperationCanceledException) {
      state.Outcomes[key] = "failed";
      Enter("Failed", "failed", $"If '{key}' condition evaluation failed: {ex.Message}");
      AdvanceFrom(sequence, state, path);
      return;
    }

    var branchName = condition ? StepPath.Body : StepPath.Else;
    var label = condition ? "then" : "else";
    var branch = StepPath.BranchSteps(step, branchName);
    var hasSteps = branch is { Count: > 0 };
    var truth = condition ? "true" : "false";
    Enter(
      "Succeeded",
      hasSteps ? label : "none",
      hasSteps
        ? $"If '{key}': {description.Detail} evaluated to {truth} → {label} branch"
        : $"If '{key}': {description.Detail} evaluated to {truth} → no {label} branch (no-op)");

    if (!hasSteps) {
      state.Outcomes[key] = "skipped";
      AdvanceFrom(sequence, state, path);
      return;
    }

    state.Outcomes[key] = "success";
    state.Frames.Add(new Frame(path, condition ? FrameKind.IfThen : FrameKind.IfElse, 1, null));
    state.Cursor = StepPath.Child(path, branchName, 0);
  }
}
