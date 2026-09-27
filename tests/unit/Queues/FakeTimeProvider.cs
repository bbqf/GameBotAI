using System;

namespace GameBot.UnitTests.Queues;

/// <summary>
/// Minimal controllable <see cref="TimeProvider"/> for deterministic time in queue-execution tests
/// (feature 059). The local time zone is UTC by default, so <see cref="TimeProvider.GetLocalNow"/>
/// tracks <see cref="GetUtcNow"/> exactly. A test can give a different local time zone (feature 109). Kept in-repo to avoid taking a dependency on an external
/// test package (plan constraint: "no new external packages").
/// </summary>
internal sealed class FakeTimeProvider : TimeProvider {
  private DateTimeOffset _utcNow;
  private readonly TimeZoneInfo _localTimeZone;

  public FakeTimeProvider(DateTimeOffset start, TimeZoneInfo? localTimeZone = null) {
    _utcNow = start;
    _localTimeZone = localTimeZone ?? TimeZoneInfo.Utc;
  }

  public override DateTimeOffset GetUtcNow() => _utcNow;

  public override TimeZoneInfo LocalTimeZone => _localTimeZone;

  /// <summary>Moves the clock forward by <paramref name="delta"/>.</summary>
  public void Advance(TimeSpan delta) => _utcNow = _utcNow.Add(delta);

  /// <summary>Sets the absolute current instant.</summary>
  public void SetUtcNow(DateTimeOffset now) => _utcNow = now;
}
