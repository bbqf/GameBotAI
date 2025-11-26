using GameBot.Domain.Triggers.Evaluators;
using FluentAssertions;
using Xunit;

namespace GameBot.UnitTests.Ocr;

public sealed class TesseractTsvParserRowsTests {
  private static string ReadFixture(string name) => File.ReadAllText(Path.Combine("tests","TestAssets","ocr","tsv", name));

  [Fact]
  public void Parse_ClearTextFixture_ParsesAllTokensAndAggregateMean() {
    var tsv = ReadFixture("clear_text.tsv");
    var tokens = TesseractTsvParser.Parse(tsv, out var agg, out var reason);
    tokens.Count.Should().Be(4);
    tokens.Select(t => t.Confidence).Should().BeEquivalentTo(new[]{95,93,92,90});
    agg.Should().BeApproximately(92.5, 0.0001);
    reason.Should().BeNull();
  }
}
