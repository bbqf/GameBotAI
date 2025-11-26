using GameBot.Domain.Triggers.Evaluators;
using FluentAssertions;
using Xunit;

namespace GameBot.UnitTests.Ocr;

public sealed class TesseractTsvParserMalformedTests {
  private static string ReadFixture(string name) => File.ReadAllText(Path.Combine("tests","TestAssets","ocr","tsv", name));

  [Fact]
  public void Parse_MalformedFixture_SkipsInvalidRowsAndAggregatesValid() {
    var tsv = ReadFixture("malformed.tsv");
    var tokens = TesseractTsvParser.Parse(tsv, out var agg, out var reason);
    tokens.Count.Should().Be(1); // only TEXT row valid
    tokens[0].Text.Should().Be("TEXT");
    agg.Should().Be(50);
    reason.Should().BeNull();
  }
}
