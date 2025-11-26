using GameBot.Domain.Triggers.Evaluators;
using FluentAssertions;
using Xunit;

namespace GameBot.UnitTests.Ocr;

public sealed class TesseractTsvParserAggregationTests {
  private static string ReadFixture(string name) => File.ReadAllText(Path.Combine("tests","TestAssets","ocr","tsv", name));

  [Fact]
  public void Parse_MixedQualityFixture_ExcludesNoiseAndComputesMean() {
    var tsv = ReadFixture("mixed_quality.tsv");
    var tokens = TesseractTsvParser.Parse(tsv, out var agg, out var reason);
    tokens.Count.Should().Be(5); // includes noise token (#) with -1
    agg.Should().BeApproximately(68.75, 0.0001); // (88+55+40+92)/4
    reason.Should().BeNull();
    tokens.Where(t => t.Confidence >= 0).Count().Should().Be(4);
    tokens.Single(t => t.Text == "#").Confidence.Should().Be(-1);
  }
}
