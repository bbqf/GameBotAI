using FluentAssertions;
using GameBot.Domain.Commands;
using Xunit;

namespace GameBot.UnitTests.Sequences;

/// <summary>Feature 170: the allowed values of <c>onTimeLimit</c> and the shared error message.</summary>
public sealed class SequenceTimeLimitsOnTimeLimitTests {
  [Theory]
  [InlineData(null, true)]
  [InlineData("fail", true)]
  [InlineData("end", true)]
  [InlineData("", false)]
  [InlineData("stop", false)]
  [InlineData("End", false)]
  [InlineData("FAIL", false)]
  public void IsValidOnTimeLimitAcceptsOnlyTheAllowedValues(string? value, bool expected) {
    SequenceTimeLimits.IsValidOnTimeLimit(value).Should().Be(expected);
  }

  [Fact]
  public void TheAllowedValuesAreFailAndEnd() {
    SequenceTimeLimits.OnTimeLimitValues.Should().Equal("fail", "end");
  }

  [Fact]
  public void TheErrorMessageNamesTheFieldAndListsTheValues() {
    SequenceTimeLimits.OnTimeLimitInvalidMessage.Should()
      .Be("onTimeLimit must be one of: fail, end.")
      .And.Contain("onTimeLimit")
      .And.Contain("fail, end");
  }

  [Fact]
  public void TheDefaultTimeLimitDoesNotChange() {
    SequenceTimeLimits.DefaultWatchdogTimeoutMs.Should().Be(240000);
    SequenceTimeLimits.Resolve(null).Should().Be(240000);
  }
}
