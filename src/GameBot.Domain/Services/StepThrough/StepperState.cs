using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using GameBot.Domain.Commands;

namespace GameBot.Domain.Services.StepThrough;

/// <summary>
/// The mutable state of one step-through: the cursor, the frame stack, the carried values, and the
/// history (data-model.md). The stepper changes it. The service owns it and guards it with a lock.
/// </summary>
public sealed class StepperState {
  /// <summary>The most history entries that a step-through keeps. The oldest entry drops first.</summary>
  public const int HistoryCap = 1000;

  private readonly List<HistoryEntry> _history = new();
  private int _nextSeq = 1;

  /// <summary>
  /// The path of the next step. It is null when the sequence is complete. It can be a loop or if step:
  /// the stepper then evaluates the entry or the next iteration when the author runs the next step.
  /// </summary>
  public string? Cursor { get; set; }

  /// <summary>The open loops and the open if branches, outermost first.</summary>
  public Collection<Frame> Frames { get; } = new();

  /// <summary>The step outcomes that conditions read. Author values and step results share this map.</summary>
  public Dictionary<string, string> Outcomes { get; } = new(StringComparer.OrdinalIgnoreCase);

  /// <summary>The values that the author gave for the sequence parameters.</summary>
  public Dictionary<string, string> ParameterValues { get; } = new(StringComparer.Ordinal);

  /// <summary>The history, ordered by <see cref="HistoryEntry.Seq"/> ascending.</summary>
  public IReadOnlyList<HistoryEntry> History => _history;

  /// <summary>
  /// Adds an entry and gives it the next sequence number. Drops the oldest entry when the cap is full.
  /// Returns the stored entry.
  /// </summary>
  public HistoryEntry Append(HistoryEntry entry) {
    ArgumentNullException.ThrowIfNull(entry);
    var stored = entry with { Seq = _nextSeq++ };
    _history.Add(stored);
    if (_history.Count > HistoryCap) {
      _history.RemoveRange(0, _history.Count - HistoryCap);
    }

    return stored;
  }

  /// <summary>Sets the execution log id on the stored entry with the given sequence number.</summary>
  public void SetExecutionLogId(int seq, string executionLogId) {
    var index = _history.FindIndex(e => e.Seq == seq);
    if (index >= 0) {
      _history[index] = _history[index] with { ExecutionLogId = executionLogId };
    }
  }

  /// <summary>
  /// Restarts the step-through (FR-009): clears the history, the frames, and the outcomes, and puts the
  /// cursor on the first step. It keeps the parameter values. A break step that does not run reads as
  /// no_break, as in a real run (feature 117).
  /// </summary>
  public void Restart(CommandSequence sequence) {
    ArgumentNullException.ThrowIfNull(sequence);
    _history.Clear();
    Frames.Clear();
    Outcomes.Clear();
    BreakStepIndex.SeedNoBreakOutcomes(Outcomes, sequence.Steps);
    Cursor = sequence.Steps.Count > 0 ? StepPath.Root(0) : null;
  }

  /// <summary>
  /// Makes a deep copy of the state. The service runs the stepper on a copy, so a read of the state during a
  /// step sees a consistent state. It then adopts the copy with <see cref="Adopt"/> when the step ends.
  /// </summary>
  public StepperState Clone() {
    var copy = new StepperState { Cursor = Cursor, _nextSeq = _nextSeq };
    foreach (var frame in Frames) copy.Frames.Add(frame);
    foreach (var pair in Outcomes) copy.Outcomes[pair.Key] = pair.Value;
    foreach (var pair in ParameterValues) copy.ParameterValues[pair.Key] = pair.Value;
    copy._history.AddRange(_history);
    return copy;
  }

  /// <summary>Replaces the content of this state with the content of <paramref name="source"/>.</summary>
  public void Adopt(StepperState source) {
    ArgumentNullException.ThrowIfNull(source);
    Cursor = source.Cursor;
    _nextSeq = source._nextSeq;
    Frames.Clear();
    foreach (var frame in source.Frames) Frames.Add(frame);
    Outcomes.Clear();
    foreach (var pair in source.Outcomes) Outcomes[pair.Key] = pair.Value;
    ParameterValues.Clear();
    foreach (var pair in source.ParameterValues) ParameterValues[pair.Key] = pair.Value;
    _history.Clear();
    _history.AddRange(source._history);
  }

  /// <summary>The entries with a sequence number greater than <paramref name="afterSeq"/>.</summary>
  public IReadOnlyList<HistoryEntry> HistoryAfter(int afterSeq)
    => _history.Where(e => e.Seq > afterSeq).ToList();
}
