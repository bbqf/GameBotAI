using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

#pragma warning disable CA2007, CA1861

namespace GameBot.ContractTests.Sequences;

/// <summary>
/// The API contract of the matchedImage condition (feature 181): save, read back, reject a bad reference,
/// and the OpenAPI description.
/// </summary>
public sealed class MatchedImageContractTests {
  private static WebApplicationFactory<Program> CreateFactory() {
    Environment.SetEnvironmentVariable("GAMEBOT_USE_ADB", "false");
    Environment.SetEnvironmentVariable("GAMEBOT_DYNAMIC_PORT", "true");
    Environment.SetEnvironmentVariable("GAMEBOT_AUTH_TOKEN", "test-token");
    return new WebApplicationFactory<Program>();
  }

  private static HttpClient Client(WebApplicationFactory<Program> app) {
    var client = app.CreateClient();
    client.DefaultRequestHeaders.Add("Authorization", "Bearer test-token");
    return client;
  }

  private static async Task<string> CreateCommandAsync(HttpClient client) {
    var response = await client.PostAsJsonAsync("/api/commands", new {
      name = "mi-" + Guid.NewGuid().ToString("N"),
      steps = new object[] { new { type = "KeyInput", order = 1, keyInput = new { key = "Enter" } } }
    });
    var body = await response.Content.ReadAsStringAsync();
    response.StatusCode.Should().Be(HttpStatusCode.Created, body);
    return JsonDocument.Parse(body).RootElement.GetProperty("id").GetString()!;
  }

  private static object CommandStep(string stepId, string commandId, object? condition = null) => condition is null
    ? new { stepId, stepType = "Action", primitiveAction = new { type = "command", schemaVersion = "v1", payload = new { commandId } } }
    : new { stepId, stepType = "Action", primitiveAction = new { type = "command", schemaVersion = "v1", payload = new { commandId } }, condition };

  private static object Leaf(string stepRef = "first", string imageId = "quest-a", bool negate = false) =>
    new { type = "matchedImage", stepRef, imageId, negate };

  [Fact]
  public async Task AStepConditionSavesAndReadsBack() {
    using var app = CreateFactory();
    var client = Client(app);
    var commandId = await CreateCommandAsync(client);

    var response = await client.PostAsJsonAsync("/api/sequences", new {
      name = "mi-" + Guid.NewGuid().ToString("N"), version = 1,
      steps = new[] { CommandStep("first", commandId), CommandStep("next", commandId, Leaf(negate: true)) }
    });
    var text = await response.Content.ReadAsStringAsync();
    response.StatusCode.Should().Be(HttpStatusCode.Created, text);
    var id = JsonDocument.Parse(text).RootElement.GetProperty("id").GetString()!;

    var read = JsonDocument.Parse(await client.GetStringAsync(new Uri($"/api/sequences/{id}", UriKind.Relative))).RootElement;
    var leaf = read.GetProperty("steps")[1].GetProperty("condition");
    leaf.GetProperty("type").GetString().Should().Be("matchedImage");
    leaf.GetProperty("stepRef").GetString().Should().Be("first");
    leaf.GetProperty("imageId").GetString().Should().Be("quest-a");
    leaf.GetProperty("negate").GetBoolean().Should().BeTrue();
  }

  [Theory]
  [InlineData("unknown", "quest-a", "unknown prior step 'unknown'")]
  [InlineData("", "quest-a", "requires stepRef")]
  [InlineData("first", "", "requires imageId")]
  public async Task ABadReferenceOrEmptyFieldIsRejectedWithAClearMessage(string stepRef, string imageId, string expected) {
    using var app = CreateFactory();
    var client = Client(app);
    var commandId = await CreateCommandAsync(client);

    var response = await client.PostAsJsonAsync("/api/sequences", new {
      name = "mi-bad-" + Guid.NewGuid().ToString("N"), version = 1,
      steps = new[] { CommandStep("first", commandId), CommandStep("next", commandId, Leaf(stepRef, imageId)) }
    });
    var text = await response.Content.ReadAsStringAsync();
    response.StatusCode.Should().Be(HttpStatusCode.BadRequest, text);
    text.Should().Contain(expected);
  }

  [Fact]
  public async Task TheOpenApiDocumentDescribesTheCondition() {
    using var app = CreateFactory();
    var client = app.CreateClient();
    var response = await client.GetAsync(new Uri("/swagger/v1/swagger.json", UriKind.Relative));
    response.EnsureSuccessStatusCode();
    using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
    var schemas = document.RootElement.GetProperty("components").GetProperty("schemas");

    schemas.TryGetProperty("MatchedImageCondition", out var schema).Should().BeTrue();
    schema.GetProperty("description").GetString().Should().Contain("type: \"matchedImage\"").And.Contain("no matched image");
    foreach (var field in new[] { "stepRef", "imageId", "negate" }) {
      schema.GetProperty("properties").GetProperty(field).GetProperty("description").GetString().Should().NotBeNullOrWhiteSpace();
    }
  }
}
