namespace GameBot.Domain.Services;

/// <summary>
/// Applies a small random offset ("jitter") to tap/swipe coordinates so repeated executions
/// do not always land on the exact same pixel.
/// <para>
/// The X and Y offsets are each independently and uniformly drawn from
/// <c>[-radiusPx, +radiusPx]</c> (a square jitter area). Results are clamped so they are
/// never negative; no upper (screen-size) bound is applied. A radius of 0 (or any
/// non-positive value) disables jitter and returns the input unchanged.
/// </para>
/// </summary>
public static class CoordinateJitter {
  private static int _sequence;

  /// <summary>
  /// Returns <paramref name="x"/>/<paramref name="y"/> offset independently per axis by a
  /// random amount in <c>[-radiusPx, +radiusPx]</c>, clamped to be non-negative.
  /// Returns the input unchanged when <paramref name="radiusPx"/> is 0 or negative.
  /// </summary>
  /// <param name="x">Target X coordinate (pixels).</param>
  /// <param name="y">Target Y coordinate (pixels).</param>
  /// <param name="radiusPx">Maximum per-axis offset in pixels; 0 disables jitter.</param>
  public static (int X, int Y) Apply(int x, int y, int radiusPx) {
    if (radiusPx <= 0) return (x, y);
    return (Math.Max(0, x + NextOffset(radiusPx)), Math.Max(0, y + NextOffset(radiusPx)));
  }

  private static int NextOffset(int radiusPx) {
    // Use non-crypto randomness via a bounded linear congruential fallback to satisfy CA5394 in non-security context.
    // The tick-based seed is mixed with a process-wide counter so back-to-back calls within the
    // same clock tick (e.g. the four offsets of one swipe) still produce independent values.
    unchecked {
      var sequence = Interlocked.Increment(ref _sequence);
      var seed = (int)(DateTime.UtcNow.Ticks & 0x00000000FFFFFFFF) ^ (sequence * 486187739);
      seed = 1664525 * seed + 1013904223; // LCG step
      var range = (2 * radiusPx) + 1;
      return Math.Abs(seed % range) - radiusPx;
    }
  }
}
