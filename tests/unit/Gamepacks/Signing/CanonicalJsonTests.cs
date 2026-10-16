using System.Text.Json;
using FluentAssertions;
using GameBot.Domain.Gamepacks.Signing;
using Xunit;

#pragma warning disable CA2007, CA1861, CA1707

namespace GameBot.UnitTests.Gamepacks.Signing;

/// <summary>Feature 146 (T007, clarification A11): the canonical JSON writer.</summary>
public sealed class CanonicalJsonTests {
  private static string Canon(string json) {
    using var doc = JsonDocument.Parse(json);
    return CanonicalJson.ToText(doc.RootElement);
  }

  [Fact]
  public void KeysAreSortedByOrdinalCompareAndNoWhiteSpaceIsWritten() {
    Canon("{ \"b\": 1, \"B\": 2, \"a\": [ 1, 2 ] }").Should().Be("{\"B\":2,\"a\":[1,2],\"b\":1}");
  }

  [Fact]
  public void EqualContentGivesEqualBytes() {
    Canon("{\"x\":{\"b\":true,\"a\":null},\"y\":\"t\"}").Should().Be(Canon("{ \"y\" : \"t\", \"x\" : { \"a\" : null, \"b\" : true } }"));
  }

  [Fact]
  public void StringEscapesFollowTheContract() {
    var text = Canon("{\"s\":\"q\\\" b\\\\ n\\n r\\r t\\t f\\f b\\b c\\u0001 e\\u00e9 s\\/\"}");
    text.Should().Be("{\"s\":\"q\\\" b\\\\ n\\n r\\r t\\t f\\f b\\b c\\u0001 eé s/\"}");
  }

  [Theory]
  [InlineData("1", "1")]
  [InlineData("1.0", "1")]
  [InlineData("-5", "-5")]
  [InlineData("1e2", "100")]
  [InlineData("0.5", "0.5")]
  [InlineData("1.5e-7", "1.5e-07")]
  public void NumbersUseOneForm(string input, string expected) {
    Canon("[" + input + "]").Should().Be("[" + expected + "]");
  }
}
