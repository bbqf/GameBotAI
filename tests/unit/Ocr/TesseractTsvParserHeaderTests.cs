using GameBot.Domain.Triggers.Evaluators;
using FluentAssertions;
using Xunit;

namespace GameBot.UnitTests.Ocr;

public sealed class TesseractTsvParserHeaderTests {
  [Fact]
  public void Parse_MissingConfHeader_ReturnsEmptyAndFormatReason() {
    var tsv = "level\tpage_num\tother\ttext"; // header without conf
    var tokens = TesseractTsvParser.Parse(tsv + "\n", out var agg, out var reason);
    tokens.Should().BeEmpty();
    agg.Should().Be(0);
    reason.Should().Be("tsv_format_unexpected");
  }
}
