using System;
using FluentAssertions;
using GameBot.Service.Services.QueueExecution;
using Xunit;

// Test-code analyzer relaxations (permitted by the constitution for test code).
#pragma warning disable CA2007, CA1861, CA1859

namespace GameBot.UnitTests.Queues;

public sealed class RelativeOffsetValidationTests {
  [Theory]
  [InlineData("00:00:00", 0)]
  [InlineData("00:10:00", 600)]
  [InlineData("01:02:03", 3723)]
  [InlineData("24:00:00", 86400)] // inclusive upper bound
  public void ParsesValidOffsets(string raw, int expectedSeconds) {
    RelativeOffsetParser.TryParse(raw, out var offset, out var error).Should().BeTrue();
    error.Should().BeNull();
    offset.Should().Be(TimeSpan.FromSeconds(expectedSeconds));
  }

  [Theory]
  [InlineData(null)]
  [InlineData("")]
  [InlineData("   ")]
  [InlineData("10:00")]     // wrong shape (missing seconds)
  [InlineData("00:60:00")]  // minutes out of range
  [InlineData("00:00:60")]  // seconds out of range
  [InlineData("5pm")]       // not a duration
  [InlineData("24:00:01")]  // just over the maximum
  [InlineData("25:00:00")]  // well over the maximum
  public void RejectsInvalidOffsets(string? raw) {
    RelativeOffsetParser.TryParse(raw, out _, out var error).Should().BeFalse();
    error.Should().NotBeNullOrEmpty();
  }

  [Fact]
  public void NegativeOffsetIsRejectedWithNonNegativeHint() {
    RelativeOffsetParser.TryParse("-00:10:00", out _, out var error).Should().BeFalse();
    error.Should().Contain("non-negative");
  }

  [Fact]
  public void FormatRoundTripsThroughParse() {
    RelativeOffsetParser.TryParse("00:10:00", out var offset, out _).Should().BeTrue();
    RelativeOffsetParser.Format(offset).Should().Be("00:10:00");
  }

  [Fact]
  public void FormatRendersTheFullDayUpperBound() {
    RelativeOffsetParser.Format(TimeSpan.FromHours(24)).Should().Be("24:00:00");
  }
}
