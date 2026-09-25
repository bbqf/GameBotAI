using System;
using FluentAssertions;
using GameBot.Domain.Queues;
using Xunit;

namespace GameBot.UnitTests.Queues;

/// <summary>
/// Feature 105: the run statistics of one (queue, sequence) pair — last-run values, counters, the cap
/// of the recent-run list, and the window check.
/// </summary>
public sealed class SequenceRunStatisticsTests {
  private static readonly DateTimeOffset T0 = new(2026, 9, 24, 12, 0, 0, TimeSpan.FromHours(2));

  private static SequenceRunRecord Record(int minute, SequenceRunStatus status) =>
    new() { StartedAt = T0.AddMinutes(minute), EndedAt = T0.AddMinutes(minute).AddSeconds(30), Status = status };

  [Fact]
  public void OneRecordSetsTheLastRunFieldsAndOneCounter() {
    var stats = new SequenceRunStatistics();
    var record = Record(0, SequenceRunStatus.Failure);

    stats.Apply(record);

    stats.LastRunStartedAt.Should().Be(record.StartedAt);
    stats.LastRunEndedAt.Should().Be(record.EndedAt);
    stats.LastRunStatus.Should().Be(SequenceRunStatus.Failure);
    stats.FailureCount.Should().Be(1);
    stats.SuccessCount.Should().Be(0);
    stats.CancelledCount.Should().Be(0);
    stats.LastSuccessAt.Should().BeNull();
    stats.RecentRuns.Should().ContainSingle();
  }

  [Fact]
  public void A101stRecordRemovesTheOldestAndTheCountersKeepCounting() {
    var stats = new SequenceRunStatistics();
    for (var i = 0; i < 101; i++) stats.Apply(Record(i, SequenceRunStatus.Success));

    stats.RecentRuns.Should().HaveCount(SequenceRunStatistics.MaxRecentRuns);
    stats.RecentRuns[0].StartedAt.Should().Be(T0.AddMinutes(1));
    stats.RecentRuns[^1].StartedAt.Should().Be(T0.AddMinutes(100));
    stats.SuccessCount.Should().Be(101);
  }

  [Theory]
  [InlineData(SequenceRunStatus.Failure)]
  [InlineData(SequenceRunStatus.Cancelled)]
  public void LastSuccessAtDoesNotChangeOnANonSuccessRecord(SequenceRunStatus status) {
    var stats = new SequenceRunStatistics();
    var success = Record(0, SequenceRunStatus.Success);
    stats.Apply(success);

    stats.Apply(Record(5, status));

    stats.LastSuccessAt.Should().Be(success.EndedAt);
    stats.LastRunStatus.Should().Be(status);
  }

  [Fact]
  public void TheWindowCheckIncludesBothEnds() {
    var stats = new SequenceRunStatistics();
    var record = Record(0, SequenceRunStatus.Success);
    stats.Apply(record);

    stats.HasRunInWindow(SequenceRunStatus.Success, record.EndedAt, record.EndedAt.AddHours(1)).Should().BeTrue();
    stats.HasRunInWindow(SequenceRunStatus.Success, record.EndedAt.AddHours(-1), record.EndedAt).Should().BeTrue();
    stats.HasRunInWindow(SequenceRunStatus.Success, record.EndedAt.AddTicks(1), record.EndedAt.AddHours(1)).Should().BeFalse();
    stats.HasRunInWindow(SequenceRunStatus.Success, record.EndedAt.AddHours(-1), record.EndedAt.AddTicks(-1)).Should().BeFalse();
  }

  [Fact]
  public void ARecordWithTheWrongStatusDoesNotMatch() {
    var stats = new SequenceRunStatistics();
    var record = Record(0, SequenceRunStatus.Failure);
    stats.Apply(record);

    stats.HasRunInWindow(SequenceRunStatus.Success, record.EndedAt.AddHours(-1), record.EndedAt.AddHours(1)).Should().BeFalse();
    stats.HasRunInWindow(SequenceRunStatus.Failure, record.EndedAt.AddHours(-1), record.EndedAt.AddHours(1)).Should().BeTrue();
  }

  [Fact]
  public void CloneIsADeepCopy() {
    var stats = new SequenceRunStatistics();
    stats.Apply(Record(0, SequenceRunStatus.Success));

    var copy = stats.Clone();
    copy.Apply(Record(1, SequenceRunStatus.Failure));

    stats.RecentRuns.Should().ContainSingle();
    stats.FailureCount.Should().Be(0);
  }
}
