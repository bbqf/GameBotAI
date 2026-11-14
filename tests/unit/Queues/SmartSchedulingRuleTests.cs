using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using GameBot.Domain.Queues;
using Xunit;

#pragma warning disable CA1861, CA5394

namespace GameBot.UnitTests.Queues;

/// <summary>Feature 189: the decision table of contracts/scheduling-rule.md.</summary>
public sealed class SmartSchedulingRuleTests {
  private static readonly TimeSpan Offset = TimeSpan.FromHours(1);
  private static readonly TimeSpan Window15 = TimeSpan.FromMinutes(15);

  private static DateTimeOffset At(int hour, int minute, int second = 0, int day = 13) =>
    new(2026, 11, day, hour, minute, second, Offset);

  private static SmartCandidate Candidate(DateTimeOffset dueAt, string sequenceId = "seq-A",
      SmartTargetKind kind = SmartTargetKind.SelfReschedule) => new(dueAt, sequenceId, kind);

  private static SmartSchedulingDecision Resolve(DateTimeOffset requested, TimeSpan window, params SmartCandidate[] candidates) =>
    SmartSchedulingRule.Resolve(requested, window, candidates);

  [Fact]
  public void Row1ACandidateInTheWindowIsTheTarget() {
    var target = Candidate(At(12, 10));
    var decision = Resolve(At(12, 0), Window15, target);
    decision.BookedAt.Should().Be(At(12, 10));
    decision.Target.Should().Be(target);
  }

  [Fact]
  public void Row2ACandidateAfterTheWindowIsNotATarget() {
    var decision = Resolve(At(12, 0), Window15, Candidate(At(12, 20)));
    decision.BookedAt.Should().Be(At(12, 0));
    decision.Target.Should().BeNull();
  }

  [Fact]
  public void Row3ACandidateBeforeTheRequestedTimeIsNotATarget() {
    var decision = Resolve(At(12, 0), Window15, Candidate(At(11, 55)));
    decision.BookedAt.Should().Be(At(12, 0));
    decision.Target.Should().BeNull();
  }

  [Fact]
  public void Row4TheEarliestCandidateInTheWindowIsTheTarget() {
    var decision = Resolve(At(12, 0), Window15, Candidate(At(12, 12), "seq-B"), Candidate(At(12, 4), "seq-C"));
    decision.BookedAt.Should().Be(At(12, 4));
    decision.Target!.SequenceId.Should().Be("seq-C");
  }

  [Fact]
  public void Row5ACandidateAtTheEndOfTheWindowIsTheTarget() {
    var decision = Resolve(At(12, 0), Window15, Candidate(At(12, 15)));
    decision.BookedAt.Should().Be(At(12, 15));
    decision.Target.Should().NotBeNull();
  }

  [Fact]
  public void Row6ACandidateOneSecondAfterTheWindowIsNotATarget() {
    var decision = Resolve(At(12, 0), Window15, Candidate(At(12, 15, 1)));
    decision.BookedAt.Should().Be(At(12, 0));
    decision.Target.Should().BeNull();
  }

  [Fact]
  public void Row7NoCandidateKeepsTheRequestedTime() {
    var decision = Resolve(At(12, 0), Window15);
    decision.BookedAt.Should().Be(At(12, 0));
    decision.Target.Should().BeNull();
  }

  [Fact]
  public void Row8ACandidateAtTheRequestedTimeIsNotATarget() {
    var decision = Resolve(At(12, 0), Window15, Candidate(At(12, 0)));
    decision.BookedAt.Should().Be(At(12, 0));
    decision.Target.Should().BeNull();
  }

  [Fact]
  public void Row9ACandidateAfterASmallerWindowIsNotATarget() {
    var decision = Resolve(At(12, 0), TimeSpan.FromMinutes(5), Candidate(At(12, 10)));
    decision.BookedAt.Should().Be(At(12, 0));
    decision.Target.Should().BeNull();
  }

  [Fact]
  public void Row10ACandidateOnTheNextDayIsATarget() {
    var decision = Resolve(At(23, 55), Window15, Candidate(At(0, 5, day: 14)));
    decision.BookedAt.Should().Be(At(0, 5, day: 14));
    decision.Target.Should().NotBeNull();
  }

  [Fact]
  public void ForEqualDueTimesTheFirstCandidateInListOrderIsTheTarget() {
    var decision = Resolve(At(12, 0), Window15,
      Candidate(At(12, 8), "first", SmartTargetKind.SelfReschedule),
      Candidate(At(12, 8), "second", SmartTargetKind.TemplateTimer));
    decision.Target!.SequenceId.Should().Be("first");
    decision.Target.Kind.Should().Be(SmartTargetKind.SelfReschedule);
  }

  [Fact]
  public void TheBookedTimeIsAlwaysFromTheRequestedTimeToTheEndOfTheWindow() {
    var random = new Random(189);
    for (var round = 0; round < 500; round++) {
      var requested = At(12, 0).AddSeconds(random.Next(-3600, 3600));
      var window = TimeSpan.FromMinutes(random.Next(SmartSchedulingLimits.MinWindowMinutes, SmartSchedulingLimits.MaxWindowMinutes + 1));
      var candidates = Enumerable.Range(0, random.Next(0, 12))
        .Select(i => Candidate(At(12, 0).AddSeconds(random.Next(-9000, 9000)), $"seq-{i}"))
        .ToList();

      var decision = SmartSchedulingRule.Resolve(requested, window, candidates);

      decision.BookedAt.Should().BeOnOrAfter(requested);
      decision.BookedAt.Should().BeOnOrBefore(requested + window);
      if (decision.Target is null) {
        decision.BookedAt.Should().Be(requested);
      } else {
        decision.BookedAt.Should().Be(decision.Target.DueAt).And.BeAfter(requested);
        candidates.Where(c => c.DueAt > requested && c.DueAt <= requested + window)
          .Min(c => c.DueAt).Should().Be(decision.BookedAt);
      }
    }
  }

  [Fact]
  public void ThePermittedWindowIsFromOneTo120Minutes() {
    SmartSchedulingLimits.IsValidWindow(0).Should().BeFalse();
    SmartSchedulingLimits.IsValidWindow(1).Should().BeTrue();
    SmartSchedulingLimits.IsValidWindow(120).Should().BeTrue();
    SmartSchedulingLimits.IsValidWindow(121).Should().BeFalse();
    SmartSchedulingLimits.IsValidWindow(-1).Should().BeFalse();
  }
}
