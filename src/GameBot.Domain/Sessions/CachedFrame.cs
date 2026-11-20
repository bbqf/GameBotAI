using System;
using System.Drawing;
using System.Runtime.Versioning;

namespace GameBot.Domain.Sessions;

/// <summary>
/// Immutable snapshot of a single captured screenshot, held in memory per session.
/// Both PNG bytes and decoded Bitmap are cached so consumers choose their preferred format.
/// </summary>
[SupportedOSPlatform("windows")]
#pragma warning disable CA1819 // PngBytes is an immutable PNG snapshot, not a mutable array property
public sealed record CachedFrame(
    byte[] PngBytes,
    Bitmap Bitmap,
    DateTimeOffset Timestamp,
    int Width,
    int Height) {
#pragma warning restore CA1819
  /// <summary>
  /// The id of the frame (feature 197). The capture loop gives a new id to each new frame. A stale repeat
  /// keeps the id of the frame before it. The default is a new GUID in "N" format.
  /// </summary>
  public string FrameId { get; init; } = Guid.NewGuid().ToString("N");

  /// <summary>
  /// True when the frame is a stale repeat (feature 197, FR-002): after a failed capture, the device
  /// returned the same bytes again. The frame then keeps its id and its capture time.
  /// </summary>
  public bool StaleRepeat { get; init; }

  /// <summary>The time of the first capture of the current stale repeat run. Null when <see cref="StaleRepeat"/> is false.</summary>
  public DateTimeOffset? StaleRepeatSince { get; init; }
}
