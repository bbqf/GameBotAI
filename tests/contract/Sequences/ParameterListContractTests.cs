#pragma warning disable CA1861 // Test data arrays are small and used one time.
using System.Net;
using System.Text.Json;
using FluentAssertions;
using Xunit;
using static GameBot.ContractTests.Sequences.FriendlyContractHelpers;

namespace GameBot.ContractTests.Sequences;

/// <summary>Feature 179: multi-value declarations on the sequence API.</summary>
public sealed class ParameterListContractTests {
  [Fact]
  public async Task MultiValueImageDeclarationRoundTrips() {
    using var app = CreateFactory();
    var client = AuthedClient(app);

    var (response, body) = await PostSequenceAsync(client, new {
      name = "quests", type = "text", kind = "image", multiple = true, minItems = 1, maxItems = 2,
      allowedImages = new[] { "q-a", "q-b", "q-c" }
    }).ConfigureAwait(false);

    response.StatusCode.Should().Be(HttpStatusCode.Created, body.ToString());
    var declaration = body.GetProperty("parameters").EnumerateArray().Single();
    declaration.GetProperty("multiple").GetBoolean().Should().BeTrue();
    declaration.GetProperty("minItems").GetInt32().Should().Be(1);
    declaration.GetProperty("maxItems").GetInt32().Should().Be(2);
  }

  [Fact]
  public async Task MultiValueChoiceDeclarationRoundTrips() {
    using var app = CreateFactory();
    var client = AuthedClient(app);

    var (response, body) = await PostSequenceAsync(client, new {
      name = "modes", type = "text", kind = "choice", multiple = true,
      options = new[] { new { label = "Fast", value = "f" }, new { label = "Slow", value = "s" } },
      @default = "f|s"
    }).ConfigureAwait(false);

    response.StatusCode.Should().Be(HttpStatusCode.Created, body.ToString());
    body.GetProperty("parameters").EnumerateArray().Single().GetProperty("multiple").GetBoolean().Should().BeTrue();
  }

  [Fact]
  public async Task DeclarationWithoutTheFlagKeepsItsShape() {
    using var app = CreateFactory();
    var client = AuthedClient(app);

    var (response, body) = await PostSequenceAsync(client, new {
      name = "pic", type = "text", kind = "image", allowedImages = new[] { "q-a" }
    }).ConfigureAwait(false);

    response.StatusCode.Should().Be(HttpStatusCode.Created, body.ToString());
    var declaration = body.GetProperty("parameters").EnumerateArray().Single();
    declaration.TryGetProperty("multiple", out _).Should().BeFalse();
    declaration.TryGetProperty("minItems", out _).Should().BeFalse();
    declaration.TryGetProperty("maxItems", out _).Should().BeFalse();
  }

  [Fact]
  public async Task MultipleOnATextParameterIsRefused() {
    using var app = CreateFactory();
    var client = AuthedClient(app);

    var (response, body) = await PostSequenceAsync(client, new { name = "t", type = "text", multiple = true })
        .ConfigureAwait(false);

    response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    body.GetProperty("error").GetString().Should().Be("invalid_parameter_declaration");
  }

  [Fact]
  public async Task MinAboveMaxIsRefused() {
    using var app = CreateFactory();
    var client = AuthedClient(app);

    var (response, body) = await PostSequenceAsync(client, new {
      name = "q", type = "text", kind = "image", multiple = true, minItems = 3, maxItems = 2,
      allowedImages = new[] { "q-a", "q-b", "q-c" }
    }).ConfigureAwait(false);

    response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    body.GetProperty("error").GetString().Should().Be("invalid_parameter_declaration");
  }

  [Fact]
  public async Task SeparatorInAnAllowedImageIsRefused() {
    using var app = CreateFactory();
    var client = AuthedClient(app);

    var (response, body) = await PostSequenceAsync(client, new {
      name = "q", type = "text", kind = "image", multiple = true, allowedImages = new[] { "q|a" }
    }).ConfigureAwait(false);

    response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    body.GetProperty("error").GetString().Should().Be("invalid_parameter_declaration");
  }

  [Fact]
  public async Task LimitsWithoutMultipleAreRefused() {
    using var app = CreateFactory();
    var client = AuthedClient(app);

    var (response, body) = await PostSequenceAsync(client, new {
      name = "q", type = "text", kind = "image", minItems = 1, allowedImages = new[] { "q-a" }
    }).ConfigureAwait(false);

    response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    body.GetProperty("error").GetString().Should().Be("invalid_parameter_declaration");
  }
}
