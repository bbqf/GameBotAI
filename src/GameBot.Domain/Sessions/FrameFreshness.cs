using System;
using System.Runtime.Versioning;

namespace GameBot.Domain.Sessions;

/// <summary>
/// The freshness rule of a frame in memory (feature 197, FR-003). A frame is stale when its age is more
/// than the effective limit, or when it is a stale repeat. The effective limit is
/// <c>max(FrameMaxAgeMs, 2 × capture interval)</c>.
/// </summary>
public static class FrameFreshness {
  /// <summary>
  /// The effective limit: <c>max(frameMaxAgeMs, 2 × captureIntervalMs)</c>. The lower bound of two capture
  /// intervals prevents a false stale flag when the capture interval is long.
  /// </summary>
  public static int EffectiveLimitMs(int frameMaxAgeMs, int captureIntervalMs) {
    var twoIntervals = (long)Math.Max(0, captureIntervalMs) * 2;
    return (int)Math.Min(int.MaxValue, Math.Max(frameMaxAgeMs, twoIntervals));
  }

  /// <summary>The age of the frame at <paramref name="now"/> in milliseconds. Never less than 0.</summary>
  [SupportedOSPlatform("windows")]
  public static long AgeMs(CachedFrame frame, DateTimeOffset now) {
    ArgumentNullException.ThrowIfNull(frame);
    return Math.Max(0L, (long)(now - frame.Timestamp).TotalMilliseconds);
  }

  /// <summary>True when the frame is a stale repeat, or its age is more than <paramref name="limitMs"/>.</summary>
  /// <param name="frame">The frame.</param>
  /// <param name="now">The time of the read.</param>
  /// <param name="limitMs">The effective limit (<see cref="EffectiveLimitMs"/>).</param>
  [SupportedOSPlatform("windows")]
  public static bool IsStale(CachedFrame frame, DateTimeOffset now, int limitMs) {
    ArgumentNullException.ThrowIfNull(frame);
    return frame.StaleRepeat || AgeMs(frame, now) > limitMs;
  }

  /// <summary>The frame id, the age, and the stale flag of the frame in one record.</summary>
  [SupportedOSPlatform("windows")]
  public static FrameReadInfo Describe(CachedFrame frame, DateTimeOffset now, int limitMs) {
    ArgumentNullException.ThrowIfNull(frame);
    return new FrameReadInfo(frame.FrameId, AgeMs(frame, now), IsStale(frame, now, limitMs));
  }
}
