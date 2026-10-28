using System.Collections.Generic;
using System.Threading;

namespace GameBot.Domain.Services;

/// <summary>
/// The states of the steps inside the commands that a sequence run has run, and the log lines of the
/// <c>commandStepOutcome</c> conditions that it has evaluated (feature 167). The scope is ambient for
/// the duration of one run, with the same pattern as <see cref="OcrTextEvaluationScope"/>: a new
/// parameter through each private method of <see cref="SequenceRunner"/> would make its large methods
/// larger. The scope lives for one run only. It is not saved.
/// </summary>
public sealed class CommandStepStateScope {
  private static readonly AsyncLocal<CommandStepStateScope?> Ambient = new();
  private readonly Dictionary<string, IReadOnlyDictionary<int, string>> _states = new(StringComparer.OrdinalIgnoreCase);
  private readonly List<string> _log = new();
  private readonly object _gate = new();

  /// <summary>The scope of the current async flow, or null.</summary>
  public static CommandStepStateScope? Current => Ambient.Value;

  /// <summary>
  /// Records the states of a sequence step that ran a command. A new run replaces the older entry. A null
  /// or empty map removes the older entry, so the latest run counts.
  /// </summary>
  public void Set(string stepKey, IReadOnlyDictionary<int, string>? states) {
    if (string.IsNullOrWhiteSpace(stepKey)) return;
    lock (_gate) {
      if (states is null || states.Count == 0) {
        _states.Remove(stepKey);
      }
      else {
        _states[stepKey] = states;
      }
    }
  }

  /// <summary>Gives the state of a command step, or null when no outcome is recorded.</summary>
  public string? TryGetState(string stepRef, int order) {
    if (string.IsNullOrWhiteSpace(stepRef)) return null;
    lock (_gate) {
      return _states.TryGetValue(stepRef, out var map) && map.TryGetValue(order, out var state) ? state : null;
    }
  }

  /// <summary>Adds one log line.</summary>
  public void AddLog(string line) {
    lock (_gate) _log.Add(line);
  }

  /// <summary>
  /// Drains the log lines of the current scope and returns them joined with "; ". Returns an empty text
  /// when no scope or no line is present.
  /// </summary>
  public static string DrainLog() {
    var scope = Current;
    if (scope is null) return string.Empty;
    lock (scope._gate) {
      if (scope._log.Count == 0) return string.Empty;
      var text = string.Join("; ", scope._log);
      scope._log.Clear();
      return text;
    }
  }

  /// <summary>Makes a new scope current when no scope is current. Returns null when a scope is already current.</summary>
  public static IDisposable? EnsureScope() => Ambient.Value is null ? Push(new CommandStepStateScope()) : null;

  /// <summary>Makes <paramref name="scope"/> current until the returned handle is disposed.</summary>
  public static IDisposable Push(CommandStepStateScope scope) {
    ArgumentNullException.ThrowIfNull(scope);
    var previous = Ambient.Value;
    Ambient.Value = scope;
    return new Restore(previous);
  }

  private sealed class Restore : IDisposable {
    private readonly CommandStepStateScope? _previous;

    public Restore(CommandStepStateScope? previous) {
      _previous = previous;
    }

    public void Dispose() => Ambient.Value = _previous;
  }
}
