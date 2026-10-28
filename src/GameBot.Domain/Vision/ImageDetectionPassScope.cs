namespace GameBot.Domain.Vision;

/// <summary>
/// The result of one image detection pass, for the timeout log line of a <c>WaitForImage</c> step
/// (feature 166, FR-012).
/// </summary>
/// <param name="FrameAvailable">True when the screen source gave a frame.</param>
/// <param name="BestScore">The best similarity of the image and its alternates. Null when no frame was available.</param>
public sealed record ImageDetectionPass(bool FrameAvailable, double? BestScore);

/// <summary>
/// Keeps the last detection pass of one <c>WaitForImage</c> wait, ambient for the duration of the wait
/// (feature 166, FR-012). The sequence runner gets only a true or false value from its condition
/// evaluator. A new parameter through each private method of the runner is a large change for one log
/// line, thus the image evaluator writes the pass into this scope. The same pattern as
/// <c>OcrTextEvaluationScope</c>.
/// <para>
/// When no scope is current, the image evaluator writes nothing, so triggers and <c>imageVisible</c>
/// conditions do not change.
/// </para>
/// </summary>
public sealed class ImageDetectionPassScope {
  private static readonly AsyncLocal<ImageDetectionPassScope?> Ambient = new();
  private ImageDetectionPass? _lastPass;

  private ImageDetectionPassScope() { }

  /// <summary>The scope of the current async flow, or null.</summary>
  public static ImageDetectionPassScope? Current => Ambient.Value;

  /// <summary>The last pass that an evaluator wrote. Null when no evaluator wrote to this scope.</summary>
  public ImageDetectionPass? LastPass {
    get => Volatile.Read(ref _lastPass);
    set => Volatile.Write(ref _lastPass, value);
  }

  /// <summary>
  /// Makes a new empty scope current until the returned handle is disposed. The dispose makes the
  /// earlier scope current again.
  /// </summary>
  public static IDisposable Push() {
    var previous = Ambient.Value;
    Ambient.Value = new ImageDetectionPassScope();
    return new Restore(previous);
  }

  private sealed class Restore : IDisposable {
    private readonly ImageDetectionPassScope? _previous;
    private bool _disposed;

    public Restore(ImageDetectionPassScope? previous) => _previous = previous;

    public void Dispose() {
      if (_disposed) return;
      _disposed = true;
      Ambient.Value = _previous;
    }
  }
}
