using System;

namespace GameBot.UnitTests.Queues;

/// <summary>
/// Minimal controllable <see cref="TimeProvider"/> for deterministic time in queue-execution tests
/// (feature 059). The local time zone is fixed to UTC so <see cref="TimeProvider.GetLocalNow"/>
/// tracks <see cref="GetUtcNow"/> exactly. Kept in-repo to avoid taking a dependency on an external
/// test package (plan constraint: "no new external packages").
/// </summary>
internal sealed class FakeTimeProvider : TimeProvider {
  private DateTimeOffset _utcNow;

  public FakeTimeProvider(DateTimeOffset start) {
    _utcNow = start;
  }

  public override DateTimeOffset GetUtcNow() => _utcNow;

  public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;

  /// <summary>Moves the clock forward by <paramref name="delta"/>.</summary>
  public void Advance(TimeSpan delta) => _utcNow = _utcNow.Add(delta);

  /// <summary>Sets the absolute current instant.</summary>
  public void SetUtcNow(DateTimeOffset now) => _utcNow = now;
}
