using FluentAssertions;
using GameBot.Domain.Triggers.Evaluators;
using Xunit;

namespace GameBot.UnitTests.TextOcr;

public class TesseractProcessOcrTextTests
{
    [Theory]
    [InlineData(null, 0)]
    [InlineData("", 0)]
    [InlineData("\0\0\0", 0)]
    [InlineData("%%%%", 0)]
    public void ComputeConfidenceReturnsZeroForMalformedStrings(string? input, double expected)
    {
        TesseractProcessOcr.ComputeConfidence(input).Should().Be(expected);
    }

    [Fact]
    public void ComputeConfidenceIgnoresNonAlphanumericNoise()
    {
        var text = "SCORE: 1234***";
        var result = TesseractProcessOcr.ComputeConfidence(text);
        result.Should().BeGreaterThan(0);
        result.Should().BeLessThan(1);
    }
}
