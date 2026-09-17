using System;
using System.Linq;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace GameBot.ContractTests.Images;

/// <summary>
/// Feature 097 (FR-013, issue #192): the alternates endpoints and the detect response's
/// <c>matchedReferenceId</c> are published in the OpenAPI document.
/// </summary>
public sealed class ImageAlternatesOpenApiTests {
  private static WebApplicationFactory<Program> CreateFactory() {
    Environment.SetEnvironmentVariable("GAMEBOT_USE_ADB", "false");
    Environment.SetEnvironmentVariable("GAMEBOT_DYNAMIC_PORT", "true");
    Environment.SetEnvironmentVariable("GAMEBOT_AUTH_TOKEN", "test-token");
    return new WebApplicationFactory<Program>();
  }

  private static async Task<JsonElement> ReadDocumentAsync() {
    using var app = CreateFactory();
    var client = app.CreateClient();
    var response = await client.GetAsync(new Uri("/swagger/v1/swagger.json", UriKind.Relative)).ConfigureAwait(false);
    response.EnsureSuccessStatusCode();
    using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync().ConfigureAwait(false));
    return document.RootElement.Clone();
  }

  [Theory]
  [InlineData("get")]
  [InlineData("put")]
  public async Task AlternatesOperationsArePublished(string method) {
    var document = await ReadDocumentAsync().ConfigureAwait(false);

    var operation = document.GetProperty("paths").GetProperty("/api/images/{id}/alternates").GetProperty(method);

    operation.GetProperty("description").GetString().Should().Contain("At most 8").And.Contain("alternates");
    var responses = operation.GetProperty("responses");
    responses.TryGetProperty("200", out _).Should().BeTrue();
    responses.TryGetProperty("400", out _).Should().BeTrue();
    responses.TryGetProperty("404", out _).Should().BeTrue();
  }

  [Fact]
  public async Task AlternatesSchemasAreDescribed() {
    var document = await ReadDocumentAsync().ConfigureAwait(false);
    var schemas = document.GetProperty("components").GetProperty("schemas");

    schemas.GetProperty("ImageAlternatesResponse").GetProperty("properties").GetProperty("alternates")
      .GetProperty("description").GetString().Should().NotBeNullOrWhiteSpace();
    schemas.GetProperty("ImageAlternateEntry").GetProperty("properties").GetProperty("exists")
      .GetProperty("description").GetString().Should().Contain("deleted");
    schemas.GetProperty("SetImageAlternatesRequest").GetProperty("properties").GetProperty("alternates")
      .GetProperty("description").GetString().Should().Contain("8");
  }

  [Fact]
  public async Task DetectResponseDescribesMatchedReferenceId() {
    var document = await ReadDocumentAsync().ConfigureAwait(false);

    var detect = document.GetProperty("paths").GetProperty("/api/images/detect").GetProperty("post");
    detect.GetProperty("description").GetString().Should().Contain("matchedReferenceId");

    var matchResult = document.GetProperty("components").GetProperty("schemas").EnumerateObject()
      .Select(p => p.Value)
      .Where(s => s.TryGetProperty("properties", out var props) && props.TryGetProperty("matchedReferenceId", out _))
      .ToList();
    matchResult.Should().NotBeEmpty("the detect response's match schema is published");
    matchResult[0].GetProperty("properties").GetProperty("matchedReferenceId").GetProperty("description").GetString()
      .Should().Contain("alternates");
  }
}
