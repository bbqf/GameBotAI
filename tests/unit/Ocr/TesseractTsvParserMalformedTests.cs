using GameBot.Domain.Triggers.Evaluators;
using FluentAssertions;
using Xunit;

namespace GameBot.UnitTests.Ocr;

public sealed class TesseractTsvParserMalformedTests {
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
  public void ParseMalformedFixtureSkipsInvalidRowsAndAggregatesValid() {
    var tsv = ReadFixture("malformed.tsv");
    var tokens = TesseractTsvParser.Parse(tsv, out var agg, out var reason);
    tokens.Count.Should().Be(1); // only TEXT row valid
    tokens[0].Text.Should().Be("TEXT");
    agg.Should().Be(50);
    reason.Should().BeNull();
  }
}
