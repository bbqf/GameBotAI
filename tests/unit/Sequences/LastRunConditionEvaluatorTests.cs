using System;
using System.IO;
using System.Threading.Tasks;
using FluentAssertions;
using GameBot.Domain.Commands;
using GameBot.Domain.Queues;
using GameBot.Domain.Services;
using GameBot.UnitTests.Queues;
using Xunit;

#pragma warning disable CA2007

namespace GameBot.UnitTests.Sequences;

/// <summary>
/// Feature 105: the <c>lastRun</c> evaluator over the run statistics of the queue (spec User Story 2).
/// The clock is a <see cref="FakeTimeProvider"/> in UTC; the store is a file store on a temp folder.
/// </summary>
public sealed class LastRunConditionEvaluatorTests : IDisposable {
  private const string Queue = "q-eval";
  private const string Own = "seq-own";
  private readonly string _root;

  public LastRunConditionEvaluatorTests() {
    _root = Path.Combine(Path.GetTempPath(), "GameBotLastRunEvalTests", Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(_root);
  }

  public void Dispose() {
    try { Directory.Delete(_root, recursive: true); } catch { /* ignore */ }
  }

  private static DateTimeOffset At(int day, int hour, int minute = 0) => new(2026, 9, day, hour, minute, 0, TimeSpan.Zero);

  private static Task RecordAsync(ISequenceRunStatisticsStore store, string sequenceId, DateTimeOffset endedAt, SequenceRunStatus status) =>
    store.RecordAsync(Queue, sequenceId, new SequenceRunRecord { StartedAt = endedAt.AddMinutes(-1), EndedAt = endedAt, Status = status });

  private static LastRunStepCondition Since(string since, string sequence = "self", string status = "success") =>
    new() { Sequence = sequence, Status = status, Since = since };

  private static LastRunStepCondition Within(string within, string sequence = "self", string status = "success") =>
    new() { Sequence = sequence, Status = status, Within = within };

  [Fact]
  public async Task Scenario1_ASuccessAfterTheSinceTimeToday() {
    using var store = new FileSequenceRunStatisticsStore(_root);
    await RecordAsync(store, Own, At(24, 12), SequenceRunStatus.Success);
    var evaluator = new LastRunConditionEvaluator(store, new FakeTimeProvider(At(24, 14)));

    (await evaluator.EvaluateAsync(Queue, Own, Since("11:00"))).Should().BeTrue();
  }

  [Fact]
  public async Task Scenario2_BeforeTheSinceTimeTheWindowStartsYesterday() {
    using var store = new FileSequenceRunStatisticsStore(_root);
    await RecordAsync(store, Own, At(23, 12), SequenceRunStatus.Success);
    var evaluator = new LastRunConditionEvaluator(store, new FakeTimeProvider(At(24, 10)));

    (await evaluator.EvaluateAsync(Queue, Own, Since("11:00"))).Should().BeTrue();
  }

  [Fact]
  public async Task Scenario3_ASuccessBeforeTheWindowDoesNotCount() {
    using var store = new FileSequenceRunStatisticsStore(_root);
    await RecordAsync(store, Own, At(24, 10, 30), SequenceRunStatus.Success);
    var evaluator = new LastRunConditionEvaluator(store, new FakeTimeProvider(At(24, 11, 30)));

    (await evaluator.EvaluateAsync(Queue, Own, Since("11:00"))).Should().BeFalse();
  }

  [Fact]
  public async Task Scenario4_OnlyFailuresInTheWindowGiveFalseForSuccess() {
    using var store = new FileSequenceRunStatisticsStore(_root);
    await RecordAsync(store, Own, At(24, 12), SequenceRunStatus.Failure);
    await RecordAsync(store, Own, At(24, 13), SequenceRunStatus.Failure);
    var evaluator = new LastRunConditionEvaluator(store, new FakeTimeProvider(At(24, 14)));

    (await evaluator.EvaluateAsync(Queue, Own, Since("11:00"))).Should().BeFalse();
    (await evaluator.EvaluateAsync(Queue, Own, Since("11:00", status: "FAILURE"))).Should().BeTrue();
  }

  [Theory]
  [InlineData(23, true)]
  [InlineData(25, false)]
  public async Task Scenario5_WithinTwentyFourHours(int hoursLater, bool expected) {
    using var store = new FileSequenceRunStatisticsStore(_root);
    await RecordAsync(store, Own, At(20, 8), SequenceRunStatus.Success);
    var evaluator = new LastRunConditionEvaluator(store, new FakeTimeProvider(At(20, 8).AddHours(hoursLater)));

    (await evaluator.EvaluateAsync(Queue, Own, Within("24:00:00"))).Should().Be(expected);
  }

  [Fact]
  public async Task Scenario6_ARecordFromBeforeARestartStillCounts() {
    using (var before = new FileSequenceRunStatisticsStore(_root)) {
      await RecordAsync(before, Own, At(24, 12), SequenceRunStatus.Success);
    }

    using var after = new FileSequenceRunStatisticsStore(_root);
    var evaluator = new LastRunConditionEvaluator(after, new FakeTimeProvider(At(24, 15)));

    (await evaluator.EvaluateAsync(Queue, Own, Since("11:00"))).Should().BeTrue();
  }

  [Fact]
  public async Task SelfNamesTheOwnSequenceAndAnIdNamesAnotherSequence() {
    using var store = new FileSequenceRunStatisticsStore(_root);
    await RecordAsync(store, "seq-other", At(24, 12), SequenceRunStatus.Success);
    var evaluator = new LastRunConditionEvaluator(store, new FakeTimeProvider(At(24, 14)));

    (await evaluator.EvaluateAsync(Queue, Own, Since("11:00"))).Should().BeFalse("self is seq-own, which has no run");
    (await evaluator.EvaluateAsync(Queue, "seq-other", Since("11:00"))).Should().BeTrue("self is seq-other here");
    (await evaluator.EvaluateAsync(Queue, Own, Since("11:00", sequence: "seq-other"))).Should().BeTrue();
  }

  [Fact]
  public async Task AnUnknownSequenceIdGivesFalse() {
    using var store = new FileSequenceRunStatisticsStore(_root);
    var evaluator = new LastRunConditionEvaluator(store, new FakeTimeProvider(At(24, 14)));

    (await evaluator.EvaluateAsync(Queue, Own, Since("11:00", sequence: "no-such-sequence"))).Should().BeFalse();
  }

  [Fact]
  public async Task NegateIsNotAppliedHere() {
    using var store = new FileSequenceRunStatisticsStore(_root);
    await RecordAsync(store, Own, At(24, 12), SequenceRunStatus.Success);
    var evaluator = new LastRunConditionEvaluator(store, new FakeTimeProvider(At(24, 14)));
    var condition = Since("11:00");
    condition.Negate = true;

    (await evaluator.EvaluateAsync(Queue, Own, condition)).Should().BeTrue();
  }

  public static TheoryData<LastRunStepCondition> StoredBadConditions => new() {
    new LastRunStepCondition { Sequence = "self", Status = "success", Since = "9:00" },
    new LastRunStepCondition { Sequence = "self", Status = "success", Within = "abc" },
    new LastRunStepCondition { Sequence = "self", Status = "success", Since = "11:00", Within = "01:00:00" },
    new LastRunStepCondition { Sequence = "self", Status = "success" },
    new LastRunStepCondition { Sequence = "self", Status = "failed", Since = "11:00" }
  };

  [Theory]
  [MemberData(nameof(StoredBadConditions))]
  public async Task AStoredBadValueThrowsUnsupportedCondition(LastRunStepCondition condition) {
    // Analyze finding G1: the save path prevents these values; a stored one fails the step loudly.
    using var store = new FileSequenceRunStatisticsStore(_root);
    var evaluator = new LastRunConditionEvaluator(store, new FakeTimeProvider(At(24, 14)));

    var act = () => evaluator.EvaluateAsync(Queue, Own, condition);

    (await act.Should().ThrowAsync<ConditionEvaluationException>())
      .Which.Kind.Should().Be(ConditionEvaluationFailureKind.UnsupportedCondition);
  }
}
