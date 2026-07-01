using System;
using FluentAssertions;
using GameBot.Domain.Commands;
using GameBot.Domain.Commands.SelfReschedule;
using Xunit;

#pragma warning disable CA2007, CA1861, CA1859

namespace GameBot.UnitTests.Sequences;

/// <summary>Feature 065: typed reading of the <c>reschedule-self</c> action payload.</summary>
public sealed class SelfReschedulePayloadTests {
  private static SequenceActionPayload Payload(params (string Key, object? Value)[] pairs) {
    var p = new SequenceActionPayload { Type = "reschedule-self" };
    foreach (var (key, value) in pairs) p.Parameters[key] = value;
    return p;
  }

  [Theory]
  [InlineData("AtQueueStart", SelfRescheduleOption.AtQueueStart)]
  [InlineData("OncePerRun", SelfRescheduleOption.OncePerRun)]
  [InlineData("Timer", SelfRescheduleOption.Timer)]
  [InlineData("EveryStep", SelfRescheduleOption.EveryStep)]
  [InlineData("onceperrun", SelfRescheduleOption.OncePerRun)] // case-insensitive
  public void ParsesKnownOptions(string wire, SelfRescheduleOption expected) {
    SelfReschedulePayload.TryRead(Payload(("option", wire)), out var result, out var error).Should().BeTrue();
    error.Should().BeNull();
    result!.Option.Should().Be(expected);
    result.TimerTimeOfDay.Should().BeNull();
    result.TimerRelativeOffset.Should().BeNull();
  }

  [Fact]
  public void ParsesTimerRelativeOffset() {
    SelfReschedulePayload.TryRead(
      Payload(("option", "Timer"), ("timerRelativeOffset", "00:10:00")),
      out var result, out _).Should().BeTrue();
    result!.Option.Should().Be(SelfRescheduleOption.Timer);
    result.HasTimerRelativeOffset.Should().BeTrue();
    result.TimerRelativeOffset.Should().Be(TimeSpan.FromMinutes(10));
    result.HasTimerTimeOfDay.Should().BeFalse();
  }

  [Fact]
  public void ParsesTimerTimeOfDay() {
    SelfReschedulePayload.TryRead(
      Payload(("option", "Timer"), ("timerTimeOfDay", "14:30:00")),
      out var result, out _).Should().BeTrue();
    result!.HasTimerTimeOfDay.Should().BeTrue();
    result.TimerTimeOfDay.Should().Be(new TimeOnly(14, 30, 0));
  }

  [Fact]
  public void MissingOptionIsRejected() {
    SelfReschedulePayload.TryRead(Payload(), out var result, out var error).Should().BeFalse();
    result.Should().BeNull();
    error.Should().NotBeNullOrWhiteSpace();
  }

  [Fact]
  public void UnknownOptionIsRejected() {
    SelfReschedulePayload.TryRead(Payload(("option", "Bogus")), out _, out var error).Should().BeFalse();
    error.Should().Contain("Bogus");
  }

  [Fact]
  public void MalformedTimerValueIsRejected() {
    SelfReschedulePayload.TryRead(
      Payload(("option", "Timer"), ("timerRelativeOffset", "not-a-duration")),
      out _, out var error).Should().BeFalse();
    error.Should().NotBeNullOrWhiteSpace();
  }
}
