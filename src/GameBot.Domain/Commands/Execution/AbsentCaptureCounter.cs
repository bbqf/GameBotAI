namespace GameBot.Domain.Commands.Execution;

/// <summary>The result of one detection pass of a <c>WaitForImage</c> step in absent mode.</summary>
public enum PassKind {
  /// <summary>The capture worked and the image is on the screen.</summary>
  Seen,

  /// <summary>The capture worked and the image is not on the screen.</summary>
  Absent,

  /// <summary>No capture was possible, or the pass gave no usable result. This is not proof of absence.</summary>
  Failed
}

/// <summary>
/// The count rule of the absent mode (feature 175). Only an absent pass adds to the count. A pass that sees
/// the image, and a failed pass, set the count to zero. This is the only place for this rule.
/// </summary>
public sealed class AbsentCaptureCounter {
  private readonly int _needed;
  private int _count;

  /// <param name="needed">The number of consecutive absent passes to reach. A value below 1 counts as 1.</param>
  public AbsentCaptureCounter(int needed) {
    _needed = Math.Max(1, needed);
  }

  /// <summary>The number of consecutive absent passes so far.</summary>
  public int Count => _count;

  /// <summary>Adds one pass to the count.</summary>
  /// <returns>True when the needed count is reached.</returns>
  public bool Observe(PassKind pass) {
    if (pass == PassKind.Absent) {
      _count++;
    }
    else {
      _count = 0;
    }

    return _count >= _needed;
  }
}
