using GameBot.Domain.Triggers.Evaluators;
using FluentAssertions;
using Xunit;

namespace GameBot.UnitTests.Ocr;

public sealed class TesseractTsvParserRowsTests {
  private static readonly int[] ExpectedClearTextConf = {95,93,92,90};
  private static string ReadFixture(string name) {
    var dir = AppContext.BaseDirectory;
    while (!string.IsNullOrEmpty(dir) && !Directory.Exists(Path.Combine(dir, "TestAssets"))) {
      var parent = Path.GetDirectoryName(dir);
      if (parent == null || parent == dir) break;
      dir = parent;
    }
    var path = Path.Combine(dir, "TestAssets", "ocr", "tsv", name);
    return File.ReadAllText(path);
  }

  [Fact]
  public void ParseClearTextFixtureParsesAllTokensAndAggregateMean() {
    var tsv = ReadFixture("clear_text.tsv");
    var tokens = TesseractTsvParser.Parse(tsv, out var agg, out var reason);
    tokens.Count.Should().Be(4);
    tokens.Select(t => t.Confidence).Should().BeEquivalentTo(ExpectedClearTextConf);
    agg.Should().BeApproximately(92.5, 0.0001);
    reason.Should().BeNull();
  }
}
