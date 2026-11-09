using System.Collections.Generic;
using System.Threading;

namespace GameBot.Domain.Services;

/// <summary>The image that matched in an image check of one step (feature 181).</summary>
/// <param name="MatchedImageId">The id of the image that matched.</param>
/// <param name="Confidence">The best similarity of the matching pass. Null when the pass gave none.</param>
public sealed record StepImageMatch(string MatchedImageId, double? Confidence);

/// <summary>
/// The latest matched image of each step that a sequence run has run (feature 181). A <c>matchedImage</c>
/// condition and a <c>{{step.&lt;stepRef&gt;.matchedImageId}}</c> placeholder read it. The scope is ambient
/// for the duration of one run, with the same pattern as <see cref="CommandStepStateScope"/>. The scope
/// lives for one run only. It is not saved.
/// </summary>
public sealed class StepImageMatchScope {
  private static readonly AsyncLocal<StepImageMatchScope?> Ambient = new();
  private readonly Dictionary<string, StepImageMatch> _matches = new(StringComparer.OrdinalIgnoreCase);
  private readonly object _gate = new();
  private StepImageMatch? _lastNoted;

  /// <summary>The scope of the current async flow, or null.</summary>
  public static StepImageMatchScope? Current => Ambient.Value;

  /// <summary>Records the matched image of a step. A new match replaces the older entry.</summary>
  public void Set(string stepKey, string imageId, double? confidence) {
    if (string.IsNullOrWhiteSpace(stepKey) || string.IsNullOrWhiteSpace(imageId)) return;
    lock (_gate) _matches[stepKey] = new StepImageMatch(imageId, confidence);
  }

  /// <summary>Removes the entry of a step, so that the latest run of the step counts.</summary>
  public void Clear(string stepKey) {
    if (string.IsNullOrWhiteSpace(stepKey)) return;
    lock (_gate) _matches.Remove(stepKey);
  }

  /// <summary>Gives the latest match of a step. Returns false when the step has no match.</summary>
  public bool TryGet(string stepKey, out StepImageMatch match) {
    match = null!;
    if (string.IsNullOrWhiteSpace(stepKey)) return false;
    lock (_gate) return _matches.TryGetValue(stepKey, out match!);
  }

  /// <summary>Gives a copy of all entries, keyed by step key.</summary>
  public IReadOnlyDictionary<string, StepImageMatch> Snapshot() {
    lock (_gate) return new Dictionary<string, StepImageMatch>(_matches, StringComparer.OrdinalIgnoreCase);
  }

  /// <summary>
  /// Notes the image that the last true image check matched. The condition evaluator calls this, and the
  /// guard of the runner reads it with <see cref="TakeNoted"/>.
  /// </summary>
  public void NoteMatch(string imageId, double? confidence) {
    lock (_gate) _lastNoted = new StepImageMatch(imageId, confidence);
  }

  /// <summary>Gives the noted match and clears it.</summary>
  public StepImageMatch? TakeNoted() {
    lock (_gate) {
      var noted = _lastNoted;
      _lastNoted = null;
      return noted;
    }
  }

  /// <summary>Makes <paramref name="scope"/> current until the returned handle is disposed.</summary>
  public static IDisposable Push(StepImageMatchScope scope) {
    ArgumentNullException.ThrowIfNull(scope);
    var previous = Ambient.Value;
    Ambient.Value = scope;
    return new Restore(previous);
  }

  private sealed class Restore : IDisposable {
    private readonly StepImageMatchScope? _previous;

    public Restore(StepImageMatchScope? previous) {
      _previous = previous;
    }

    public void Dispose() => Ambient.Value = _previous;
  }
}
