#pragma warning disable CA1861 // Test data arrays are small and used one time.
using System.Net;
using FluentAssertions;
using Xunit;
using static GameBot.ContractTests.Sequences.FriendlyContractHelpers;

namespace GameBot.ContractTests.Sequences;

/// <summary>Feature 168: choice parameters with options on the sequence API.</summary>
public sealed class FriendlyChoiceParameterContractTests {
  private static object Choice(string type, params (string Label, string Value)[] options) => new {
    name = "mode", type, kind = "choice",
    options = options.Select(o => new { label = o.Label, value = o.Value }).ToArray()
  };

  [Fact]
  public async Task OptionsSaveAndLoadAndOneOptionIsValid() {
    using var app = CreateFactory();
    var client = AuthedClient(app);

    var (response, body) = await PostSequenceAsync(client, Choice("text", ("Fast", "f"))).ConfigureAwait(false);

    response.StatusCode.Should().Be(HttpStatusCode.Created, body.ToString());
    var option = body.GetProperty("parameters")[0].GetProperty("options").EnumerateArray().Single();
    option.GetProperty("label").GetString().Should().Be("Fast");
    option.GetProperty("value").GetString().Should().Be("f");
  }

  [Fact]
  public async Task DuplicateLabelIsRefused() {
    using var app = CreateFactory();
    var client = AuthedClient(app);

    var (response, body) = await PostSequenceAsync(client, Choice("text", ("Fast", "a"), ("fast", "b")))
        .ConfigureAwait(false);

    response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    body.GetProperty("error").GetString().Should().Be("invalid_parameter_declaration");
  }

  [Fact]
  public async Task NumberChoiceWithANonNumberValueIsRefused() {
    using var app = CreateFactory();
    var client = AuthedClient(app);

    var (response, _) = await PostSequenceAsync(client, Choice("number", ("One", "1"), ("Two", "x")))
        .ConfigureAwait(false);

    response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
  }

  [Fact]
  public async Task MoreThanFiftyOptionsAreRefused() {
    using var app = CreateFactory();
    var client = AuthedClient(app);
    var options = Enumerable.Range(1, 51).Select(i => ($"L{i}", $"v{i}")).ToArray();

    var (response, _) = await PostSequenceAsync(client, Choice("text", options)).ConfigureAwait(false);

    response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
  }

  [Fact]
  public async Task WholePlaceholderOptionValueIsRefused() {
    using var app = CreateFactory();
    var client = AuthedClient(app);

    var (response, _) = await PostSequenceAsync(client, Choice("text", ("A", "{{a}}"))).ConfigureAwait(false);

    response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
  }

  [Fact]
  public async Task UnknownKindIsRefusedWithAHint() {
    using var app = CreateFactory();
    var client = AuthedClient(app);

    var (response, body) = await PostSequenceAsync(client, new { name = "x", kind = "slider" }).ConfigureAwait(false);

    response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    body.GetProperty("message").GetString().Should().Contain("Use text, number, choice, or image");
  }
}
