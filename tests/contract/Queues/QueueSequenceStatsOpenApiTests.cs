using System;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace GameBot.ContractTests.Queues;

/// <summary>
/// Feature 105 (FR-013): the OpenAPI document describes <c>sequenceStats</c> on the queue read and the
/// <c>QueueSequenceStatsResponse</c> schema.
/// </summary>
public sealed class QueueSequenceStatsOpenApiTests {
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

  private static JsonElement Schema(JsonElement document, string name)
    => document.GetProperty("components").GetProperty("schemas").GetProperty(name);

  [Fact]
  public async Task TheStatsSchemaDescribesEachField() {
    var document = await ReadDocumentAsync().ConfigureAwait(false);

    var properties = Schema(document, "QueueSequenceStatsResponse").GetProperty("properties");
    foreach (var field in new[] { "sequenceName", "lastRunStartedAt", "lastRunEndedAt", "lastRunStatus", "lastSuccessAt", "successCount", "failureCount", "cancelledCount" }) {
      properties.GetProperty(field).GetProperty("description").GetString()
        .Should().NotBeNullOrWhiteSpace($"'{field}' must have a description");
    }
  }

  [Fact]
  public async Task LastRunStatusListsItsValues() {
    var document = await ReadDocumentAsync().ConfigureAwait(false);

    Schema(document, "QueueSequenceStatsResponse").GetProperty("properties").GetProperty("lastRunStatus")
      .GetProperty("enum").EnumerateArray().Select(v => v.GetString())
      .Should().Equal("success", "failure", "cancelled");
  }

  [Fact]
  public async Task SequenceStatsIsADescribedMapOfTheStatsSchema() {
    var document = await ReadDocumentAsync().ConfigureAwait(false);

    var stats = Schema(document, "QueueDetailResponse").GetProperty("properties").GetProperty("sequenceStats");

    stats.GetProperty("type").GetString().Should().Be("object");
    stats.GetProperty("description").GetString().Should().Contain("keyed by sequence ID");
    stats.GetProperty("additionalProperties").GetProperty("$ref").GetString()
      .Should().EndWith("/QueueSequenceStatsResponse");
  }

  [Fact]
  public async Task GetQueueOperationNamesSequenceStats() {
    var document = await ReadDocumentAsync().ConfigureAwait(false);

    document.GetProperty("paths").GetProperty("/api/queues/{id}")
      .GetProperty("get").GetProperty("description").GetString()
      .Should().Contain("sequenceStats");
  }
}
