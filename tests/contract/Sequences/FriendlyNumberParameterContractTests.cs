using System.Net;
using FluentAssertions;
using Xunit;
using static GameBot.ContractTests.Sequences.FriendlyContractHelpers;

namespace GameBot.ContractTests.Sequences;

/// <summary>Feature 168: number parameters with range, unit, and pattern on the sequence API.</summary>
public sealed class FriendlyNumberParameterContractTests {
  [Fact]
  public async Task NumberSettingsSaveAndLoad() {
    using var app = CreateFactory();
    var client = AuthedClient(app);

    var (response, body) = await PostSequenceAsync(client, new {
      name = "speed", type = "number", min = 1, max = 9, unit = "x", pattern = "emulator-{value}", @default = "5"
    }).ConfigureAwait(false);

    response.StatusCode.Should().Be(HttpStatusCode.Created, body.ToString());
    var declaration = body.GetProperty("parameters")[0];
    declaration.GetProperty("min").GetInt32().Should().Be(1);
    declaration.GetProperty("max").GetInt32().Should().Be(9);
    declaration.GetProperty("unit").GetString().Should().Be("x");
    declaration.GetProperty("pattern").GetString().Should().Be("emulator-{value}");
  }

  [Theory]
  [InlineData("no marker")]
  [InlineData("{value}-{value}")]
  public async Task PatternWithoutExactlyOneMarkerIsRefused(string pattern) {
    using var app = CreateFactory();
    var client = AuthedClient(app);

    var (response, body) = await PostSequenceAsync(client, new { name = "speed", type = "number", pattern })
        .ConfigureAwait(false);

    response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    body.GetProperty("error").GetString().Should().Be("invalid_parameter_declaration");
  }

  [Fact]
  public async Task DefaultOutsideTheRangeIsRefusedWithTheDefaultCode() {
    using var app = CreateFactory();
    var client = AuthedClient(app);

    var (response, body) = await PostSequenceAsync(client, new {
      name = "speed", type = "number", min = 1, max = 5, @default = "9"
    }).ConfigureAwait(false);

    response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    body.GetProperty("error").GetString().Should().Be("invalid_parameter_default");
  }
}
