using System;
using System.Globalization;
using System.Runtime.Versioning;
using System.Threading;
using System.Threading.Tasks;

namespace GameBot.Domain.Sessions;

/// <summary>
/// The frame freshness gate of one sequence run with a session (feature 197). It is ambient for the time
/// of the run, the same pattern as <c>ImageDetectionPassScope</c> and <c>OcrTextEvaluationScope</c>.
/// <para>
/// Each place that decodes the frame in memory for a step calls <see cref="Admit"/>. With no scope,
/// <see cref="Admit"/> is true and records nothing, so triggers and the other readers do not change.
/// With a scope, it records the read in <see cref="LastRead"/> and refuses a stale frame. The reader then
/// returns no frame.
/// </para>
/// </summary>
public sealed class FrameReadScope {
  private static readonly AsyncLocal<FrameReadScope?> Ambient = new();
  private readonly Func<CancellationToken, Task<bool>>? _waitForFresh;
  private FrameReadInfo? _lastRead;

  /// <summary>Creates a scope.</summary>
  /// <param name="maxAgeMs">The effective limit (<see cref="FrameFreshness.EffectiveLimitMs"/>).</param>
  /// <param name="clock">The clock of the age calculation. <see cref="TimeProvider.System"/> when null.</param>
  /// <param name="waitForFresh">
  /// Waits for a fresh frame. Returns true when the frame is fresh or no frame exists, and false when the
  /// frame is still stale after <paramref name="maxAgeMs"/>. Null: no wait.
  /// </param>
  public FrameReadScope(int maxAgeMs, TimeProvider? clock = null, Func<CancellationToken, Task<bool>>? waitForFresh = null) {
    MaxAgeMs = Math.Max(0, maxAgeMs);
    Clock = clock ?? TimeProvider.System;
    _waitForFresh = waitForFresh;
  }

  /// <summary>The effective limit of the frame age, in milliseconds.</summary>
  public int MaxAgeMs { get; }

  /// <summary>The clock of the age calculation.</summary>
  public TimeProvider Clock { get; }

  /// <summary>The data of the last admitted or refused frame. Null before the first read.</summary>
  public FrameReadInfo? LastRead {
    get => Volatile.Read(ref _lastRead);
    set => Volatile.Write(ref _lastRead, value);
  }

  /// <summary>The scope of the current async flow, or null.</summary>
  public static FrameReadScope? Current => Ambient.Value;

  /// <summary>
  /// Makes <paramref name="scope"/> current until the returned handle is disposed. The dispose makes the
  /// earlier scope current again.
  /// </summary>
  public static IDisposable Push(FrameReadScope scope) {
    ArgumentNullException.ThrowIfNull(scope);
    var previous = Ambient.Value;
    Ambient.Value = scope;
    return new Restore(previous);
  }

  /// <summary>
  /// The gate of a frame read. No scope: true, and nothing is recorded. Scope: the read is recorded in
  /// <see cref="LastRead"/>, and the result is false for a stale frame.
  /// </summary>
  [SupportedOSPlatform("windows")]
  public static bool Admit(CachedFrame frame) {
    ArgumentNullException.ThrowIfNull(frame);
    var scope = Current;
    if (scope is null) return true;
    var info = FrameFreshness.Describe(frame, scope.Clock.GetUtcNow(), scope.MaxAgeMs);
    scope.LastRead = info;
    return !info.Stale;
  }

  /// <summary>
  /// Waits for a fresh frame with the wait delegate. True when the frame is fresh or no frame exists, or
  /// when the scope has no wait delegate. False when the frame is still stale after <see cref="MaxAgeMs"/>.
  /// </summary>
  public Task<bool> EnsureFreshAsync(CancellationToken ct) =>
    _waitForFresh is null ? Task.FromResult(true) : _waitForFresh(ct);

  /// <summary>
  /// The text of a <c>stale_frame</c> failure (contract section 5):
  /// <c>stale_frame: the frame &lt;id&gt; is &lt;n&gt; ms old (limit &lt;limit&gt; ms).</c>
  /// </summary>
  public string StaleMessage() {
    var read = LastRead;
    return string.Format(
      CultureInfo.InvariantCulture,
      "stale_frame: the frame {0} is {1} ms old (limit {2} ms).",
      read?.FrameId ?? "none",
      read?.AgeMs ?? 0,
      MaxAgeMs);
  }

  private sealed class Restore : IDisposable {
    private readonly FrameReadScope? _previous;
    private bool _disposed;

    public Restore(FrameReadScope? previous) => _previous = previous;

    public void Dispose() {
      if (_disposed) return;
      _disposed = true;
      Ambient.Value = _previous;
    }
  }
}
