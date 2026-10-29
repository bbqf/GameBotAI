#pragma warning disable CA1861 // Test data arrays are small and used one time.
using System.Net;
using System.Text.Json;
using FluentAssertions;
using Xunit;
using static GameBot.ContractTests.Sequences.FriendlyContractHelpers;

namespace GameBot.ContractTests.Sequences;

/// <summary>Feature 168: image parameters with allowed images on the sequence API.</summary>
public sealed class FriendlyImageParameterContractTests {
  [Fact]
  public async Task ImageParameterSavesAndLoadsWithAllowedImages() {
    using var app = CreateFactory();
    var client = AuthedClient(app);

    var (response, body) = await PostSequenceAsync(client, new {
      name = "pic", type = "text", kind = "image", label = "Picture", allowedImages = new[] { "img-a", "img-b" }
    }).ConfigureAwait(false);

    response.StatusCode.Should().Be(HttpStatusCode.Created, body.ToString());
    var declaration = body.GetProperty("parameters").EnumerateArray().Single();
    declaration.GetProperty("kind").GetString().Should().Be("image");
    declaration.GetProperty("label").GetString().Should().Be("Picture");
    declaration.GetProperty("allowedImages").EnumerateArray().Select(e => e.GetString())
        .Should().Equal("img-a", "img-b");
  }

  [Fact]
  public async Task EmptyAllowedImagesIsRefusedWithAHint() {
    using var app = CreateFactory();
    var client = AuthedClient(app);

    var (response, body) = await PostSequenceAsync(client, new {
      name = "pic", type = "text", kind = "image", allowedImages = Array.Empty<string>()
    }).ConfigureAwait(false);

    response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    body.GetProperty("error").GetString().Should().Be("invalid_parameter_declaration");
    body.GetProperty("message").GetString().Should().Contain("Choose");
  }

  [Fact]
  public async Task AllowedImageWithNoStoredImageGivesAWarningAndTheSaveWorks() {
    using var app = CreateFactory();
    var client = AuthedClient(app);

    var (response, body) = await PostSequenceAsync(client, new {
      name = "pic", type = "text", kind = "image", allowedImages = new[] { "no-such-image-168" }
    }).ConfigureAwait(false);

    response.StatusCode.Should().Be(HttpStatusCode.Created, body.ToString());
    HasWarning(body, "unknown_allowed_image").Should().BeTrue(body.ToString());
  }
}
