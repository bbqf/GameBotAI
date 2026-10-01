using System;
using System.Collections.Generic;
using System.Threading;
using FluentAssertions;
using GameBot.Domain.Commands;
using GameBot.Domain.Commands.SelfReschedule;
using GameBot.Service.Services.QueueExecution;
using Xunit;

#pragma warning disable CA2007, CA1861, CA1859

namespace GameBot.UnitTests.Queues;

/// <summary>Feature 125: a booking without <c>keep</c> keeps the old rule. The last booking wins.</summary>
public sealed class SelfRescheduleDefaultBehaviorTests {
  private static SequenceActionPayload Payload(params (string Key, object? Value)[] pairs) {
    var p = new SequenceActionPayload { Type = "reschedule-self" };
    foreach (var (key, value) in pairs) p.Parameters[key] = value;
    return p;
  }

  [Fact]
  public void APayloadWithNoKeepKeyReadsAsNone() {
    SelfReschedulePayload.TryRead(Payload(("option", "Timer"), ("timerRelativeOffset", "00:10:00")), out var result, out _)
      .Should().BeTrue();
    result!.Keep.Should().Be(SelfRescheduleKeep.None);
    result.HasKeep.Should().BeFalse();
  }

  [Fact]
  public void APayloadWithJsonNullKeepReadsAsNone() {
    using var doc = System.Text.Json.JsonDocument.Parse("null");
    SelfReschedulePayload.TryRead(
      Payload(("option", "Timer"), ("timerRelativeOffset", "00:10:00"), ("keep", doc.RootElement.Clone())), out var result, out _)
      .Should().BeTrue();
    result!.Keep.Should().Be(SelfRescheduleKeep.None);
    result.HasKeep.Should().BeFalse();
  }

  [Fact]
  public void TheCoordinatorWithNoKeepLetsTheLastBookingWin() {
    var registry = new QueueRunRegistry();
    var handle = new QueueRunHandle { QueueId = "q1", Cts = new CancellationTokenSource() };
    registry.TryAdd("q1", handle);
    var start = new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
    var coordinator = new SelfRescheduleCoordinator(registry, new FakeTimeProvider(start));

    foreach (var minutes in new[] { 15, 50, 30, 40 }) {
      var result = coordinator.ScheduleSelf("q1", "seq-A", SelfRescheduleOption.Timer, null, TimeSpan.FromMinutes(minutes), null, SelfRescheduleKeep.None, "run-1");
      result.KeptPending.Should().BeFalse();
    }

    handle.SnapshotPendingTimerFirings().Should().ContainSingle()
      .Which.FireAt.Should().Be(start.AddMinutes(40));
  }

  [Fact]
  public void AddTimerFiringWithKeepEarliestFalseReplacesForTheSameRunAndAnEarlierTime() {
    var handle = new QueueRunHandle { QueueId = "q1", Cts = new CancellationTokenSource() };
    var early = new DateTimeOffset(2026, 1, 1, 12, 15, 0, TimeSpan.Zero);
    var late = new DateTimeOffset(2026, 1, 1, 12, 50, 0, TimeSpan.Zero);
    handle.AddTimerFiring(new SelfRescheduleEntry("a", "seq-A", SelfRescheduleOption.Timer, early, null, "run-1"), keepEarliest: false);

    var result = handle.AddTimerFiring(new SelfRescheduleEntry("b", "seq-A", SelfRescheduleOption.Timer, late, null, "run-1"), keepEarliest: false);

    result.Kind.Should().Be(TimerBookingKind.Replaced);
    handle.SnapshotPendingTimerFirings().Should().ContainSingle().Which.FireAt.Should().Be(late);
  }
}
