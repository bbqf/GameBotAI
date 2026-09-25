using System;

namespace GameBot.Domain.Services;

/// <summary>
/// The window math of the <c>since</c> field of a <c>lastRun</c> condition (feature 105, research R-007).
/// </summary>
public static class LastRunWindow {
  // A real zone never needs more: a day can skip a time of day, but not two days in sequence.
  private const int MaxDaysBack = 2;

  /// <summary>
  /// Returns the most recent real occurrence of the local time of day <paramref name="since"/> in
  /// <paramref name="zone"/>, at or before <paramref name="now"/>. At the exact minute, the window starts
  /// now. On a spring-forward day where the time does not occur, the result is the occurrence on the
  /// day before. On a fall-back day where the time occurs two times, the result is the later of the two
  /// instants that is at or before now.
  /// </summary>
  public static DateTimeOffset SinceStart(DateTimeOffset now, TimeOnly since, TimeZoneInfo zone) {
    ArgumentNullException.ThrowIfNull(zone);
    var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, zone).DateTime);

    for (var daysBack = 0; daysBack <= MaxDaysBack; daysBack++) {
      var wallClock = today.AddDays(-daysBack).ToDateTime(since, DateTimeKind.Unspecified);
      if (zone.IsInvalidTime(wallClock)) continue;

      var candidate = MostRecentInstant(wallClock, now, zone);
      if (candidate <= now) return candidate;
    }

    // Not reachable in a real zone. A full day back is a safe, wide window.
    return now.AddDays(-1);
  }

  private static DateTimeOffset MostRecentInstant(DateTime wallClock, DateTimeOffset now, TimeZoneInfo zone) {
    if (!zone.IsAmbiguousTime(wallClock)) {
      return new DateTimeOffset(wallClock, zone.GetUtcOffset(wallClock));
    }

    DateTimeOffset? latestAtOrBeforeNow = null;
    DateTimeOffset? earliest = null;
    foreach (var offset in zone.GetAmbiguousTimeOffsets(wallClock)) {
      var instant = new DateTimeOffset(wallClock, offset);
      if (earliest is null || instant < earliest) earliest = instant;
      if (instant <= now && (latestAtOrBeforeNow is null || instant > latestAtOrBeforeNow)) latestAtOrBeforeNow = instant;
    }

    return latestAtOrBeforeNow ?? earliest!.Value;
  }
}
