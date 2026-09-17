using System;
using System.Linq;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace GameBot.ContractTests.Queues;

/// <summary>
/// Feature 096 (FR-009, issue #199): <c>health.paused</c> was read as "not paused" during an idle pause
/// partly because nothing published what it meant. The OpenAPI document must describe the pause fields
/// as covering both kinds of pause and list <c>pauseKind</c>'s values.
/// </summary>
public sealed class QueueHealthOpenApiTests {
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

  private static JsonElement HealthProperty(JsonElement document, string property)
    => document.GetProperty("components").GetProperty("schemas").GetProperty("QueueHealthResponse")
      .GetProperty("properties").GetProperty(property);

  [Fact]
  public async Task PauseFieldsDescribeBothKindsOfPause() {
    var document = await ReadDocumentAsync().ConfigureAwait(false);

    HealthProperty(document, "paused").GetProperty("description").GetString()
      .Should().Contain("idle pause").And.Contain("failure-policy pause");
    HealthProperty(document, "pausedAt").GetProperty("description").GetString().Should().NotBeNullOrWhiteSpace();
    HealthProperty(document, "pauseReason").GetProperty("description").GetString()
      .Should().Contain("idle pause: resumes at");
  }

  [Fact]
  public async Task PauseKindListsItsValues() {
    var document = await ReadDocumentAsync().ConfigureAwait(false);

    var pauseKind = HealthProperty(document, "pauseKind");

    pauseKind.GetProperty("description").GetString().Should().Contain("idle").And.Contain("failurePolicy");
    pauseKind.GetProperty("enum").EnumerateArray().Select(v => v.GetString())
      .Should().Equal("idle", "failurePolicy");
  }

  [Fact]
  public async Task GetQueueOperationPointsAtPauseKind() {
    var document = await ReadDocumentAsync().ConfigureAwait(false);

    document.GetProperty("paths").GetProperty("/api/queues/{id}")
      .GetProperty("get").GetProperty("description").GetString()
      .Should().Contain("pauseKind");
  }
}
