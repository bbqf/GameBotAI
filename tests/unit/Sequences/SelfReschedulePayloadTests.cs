using System;
using System.Collections.Generic;
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
  [InlineData("Cancel", SelfRescheduleOption.Cancel)]
  [InlineData("cancel", SelfRescheduleOption.Cancel)] // feature 123, case-insensitive
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

  [Theory]
  [InlineData("14:30", 14, 30, 0)]
  [InlineData("14:30:45", 14, 30, 45)]
  public void ParsesBothTimeOfDayForms(string text, int hour, int minute, int second) {
    SelfReschedulePayload.TryRead(
      Payload(("option", "Timer"), ("timerTimeOfDay", text)),
      out var result, out _).Should().BeTrue();
    result!.TimerTimeOfDay.Should().Be(new TimeOnly(hour, minute, second));
  }

  [Theory]
  [InlineData("11:00 PM")]
  [InlineData("9:30")]
  [InlineData(" 11:00")]
  [InlineData("24:00")]
  public void NotStrictTimeOfDayIsRejectedWithTheAcceptedFormat(string text) {
    SelfReschedulePayload.TryRead(
      Payload(("option", "Timer"), ("timerTimeOfDay", text)),
      out _, out var error).Should().BeFalse();
    error.Should().Contain("HH:mm or HH:mm:ss");
    error.Should().Contain("timerTimeOfDay");
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

  // ── feature 068: ocrOffset parsing ─────────────────────────────────────────

  private static Dictionary<string, object?> Region(int x, int y, int w, int h) =>
    new() { ["x"] = x, ["y"] = y, ["width"] = w, ["height"] = h };

  [Fact]
  public void AbsentOcrOffsetLeavesPayloadUnchanged() {
    SelfReschedulePayload.TryRead(Payload(("option", "Timer"), ("timerRelativeOffset", "00:10:00")),
      out var result, out _).Should().BeTrue();
    result!.HasOcrOffset.Should().BeFalse();
    result.OcrOffset.Should().BeNull();
  }

  [Fact]
  public void ParsesOcrOffsetWithDefaults() {
    var ocr = new Dictionary<string, object?> {
      ["region"] = Region(10, 20, 120, 40),
      ["fallback"] = "00:06:00"
    };
    SelfReschedulePayload.TryRead(Payload(("option", "Timer"), ("ocrOffset", ocr)),
      out var result, out var error).Should().BeTrue();
    error.Should().BeNull();
    result!.HasOcrOffset.Should().BeTrue();
    result.OcrOffset!.Region.Should().Be(new OcrOffsetRegion(10, 20, 120, 40));
    result.OcrOffset.Fallback.Should().Be(TimeSpan.FromMinutes(6));
    result.OcrOffset.Min.Should().Be(TimeSpan.FromSeconds(1));
    result.OcrOffset.Max.Should().Be(TimeSpan.FromHours(24));
  }

  [Fact]
  public void ParsesOcrOffsetWithExplicitBounds() {
    var ocr = new Dictionary<string, object?> {
      ["region"] = Region(1, 2, 3, 4),
      ["fallback"] = "00:06:00",
      ["min"] = "00:00:05",
      ["max"] = "01:00:00"
    };
    SelfReschedulePayload.TryRead(Payload(("option", "Timer"), ("ocrOffset", ocr)),
      out var result, out _).Should().BeTrue();
    result!.OcrOffset!.Min.Should().Be(TimeSpan.FromSeconds(5));
    result.OcrOffset.Max.Should().Be(TimeSpan.FromHours(1));
  }

  [Fact]
  public void OcrOffsetMissingRegionIsRejected() {
    var ocr = new Dictionary<string, object?> { ["fallback"] = "00:06:00" };
    SelfReschedulePayload.TryRead(Payload(("option", "Timer"), ("ocrOffset", ocr)),
      out _, out var error).Should().BeFalse();
    error.Should().Contain("region");
  }

  [Fact]
  public void OcrOffsetMissingFallbackIsRejected() {
    var ocr = new Dictionary<string, object?> { ["region"] = Region(1, 2, 3, 4) };
    SelfReschedulePayload.TryRead(Payload(("option", "Timer"), ("ocrOffset", ocr)),
      out _, out var error).Should().BeFalse();
    error.Should().Contain("fallback");
  }

  // ── Feature 125: keep ─────────────────────────────────────────────────────

  [Theory]
  [InlineData("earliest")]
  [InlineData("Earliest")]
  [InlineData("EARLIEST")]
  public void KeepEarliestParsesInAnyCase(string wire) {
    SelfReschedulePayload.TryRead(
      Payload(("option", "Timer"), ("timerRelativeOffset", "00:10:00"), ("keep", wire)), out var result, out var error).Should().BeTrue();
    error.Should().BeNull();
    result!.Keep.Should().Be(SelfRescheduleKeep.Earliest);
    result.HasKeep.Should().BeTrue();
  }

  [Fact]
  public void KeepJsonNullCountsAsAbsent() {
    using var doc = System.Text.Json.JsonDocument.Parse("null");
    SelfReschedulePayload.TryRead(
      Payload(("option", "Timer"), ("timerRelativeOffset", "00:10:00"), ("keep", doc.RootElement.Clone())), out var result, out _).Should().BeTrue();
    result!.Keep.Should().Be(SelfRescheduleKeep.None);
    result.HasKeep.Should().BeFalse();
  }

  [Theory]
  [InlineData("latest")]
  [InlineData("")]
  [InlineData("   ")]
  public void KeepWithAnotherStringValueIsRejectedAndNamesEarliest(string wire) {
    SelfReschedulePayload.TryRead(
      Payload(("option", "Timer"), ("timerRelativeOffset", "00:10:00"), ("keep", wire)), out _, out var error).Should().BeFalse();
    error.Should().Contain("earliest");
  }

  [Theory]
  [InlineData(true)]
  [InlineData(5)]
  public void KeepWithANonStringValueIsRejectedAndNamesEarliest(object wire) {
    SelfReschedulePayload.TryRead(
      Payload(("option", "Timer"), ("timerRelativeOffset", "00:10:00"), ("keep", wire)), out _, out var error).Should().BeFalse();
    error.Should().Contain("earliest");
  }

  [Fact]
  public void KeepInsideOcrOffsetIsNotRead() {
    var ocr = new Dictionary<string, object?> {
      ["region"] = Region(1, 2, 3, 4),
      ["fallback"] = "00:06:00",
      ["keep"] = "earliest"
    };
    SelfReschedulePayload.TryRead(Payload(("option", "Timer"), ("ocrOffset", ocr)), out var result, out _).Should().BeTrue();
    result!.HasKeep.Should().BeFalse();
    result.Keep.Should().Be(SelfRescheduleKeep.None);
  }

  [Fact]
  public void OcrOffsetMalformedFallbackIsRejected() {
    var ocr = new Dictionary<string, object?> {
      ["region"] = Region(1, 2, 3, 4),
      ["fallback"] = "not-a-duration"
    };
    SelfReschedulePayload.TryRead(Payload(("option", "Timer"), ("ocrOffset", ocr)),
      out _, out var error).Should().BeFalse();
    error.Should().Contain("fallback");
  }
}
