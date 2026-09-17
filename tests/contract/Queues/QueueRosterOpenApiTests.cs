using System;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace GameBot.ContractTests.Queues;

/// <summary>
/// Feature 100 (issue #179): a queue's roster is read from <c>GET /api/queues/{id}</c> <c>entries</c>, but a
/// consumer looking for <c>GET /api/queues/{id}/entries</c> could not learn that from the OpenAPI document.
/// The document must describe <c>entries</c> and its item fields, and the entries write operations must point
/// at the read path.
/// </summary>
public sealed class QueueRosterOpenApiTests {
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

  private static JsonElement SchemaProperty(JsonElement document, string schema, string property)
    => document.GetProperty("components").GetProperty("schemas").GetProperty(schema)
      .GetProperty("properties").GetProperty(property);

  private static string? Description(JsonElement element)
    => element.TryGetProperty("description", out var description) ? description.GetString() : null;

  [Fact]
  public async Task EntriesDescribesTheLiveRoster() {
    var document = await ReadDocumentAsync().ConfigureAwait(false);

    Description(SchemaProperty(document, "QueueDetailResponse", "entries"))
      .Should().Contain("roster").And.Contain("never null").And.Contain("template").And.Contain("GET /api/queues/{id}");
  }

  [Fact]
  public async Task EntriesIsNotNullableButStaysReadOnly() {
    var document = await ReadDocumentAsync().ConfigureAwait(false);

    var entries = SchemaProperty(document, "QueueDetailResponse", "entries");

    (entries.TryGetProperty("nullable", out var nullable) && nullable.GetBoolean()).Should().BeFalse();
    entries.GetProperty("readOnly").GetBoolean().Should().BeTrue();
  }

  [Fact]
  public async Task EntryFieldsAreDescribed() {
    var document = await ReadDocumentAsync().ConfigureAwait(false);

    Description(SchemaProperty(document, "QueueEntryResponse", "entryId"))
      .Should().Contain("DELETE /api/queues/{id}/entries/{entryId}");
    Description(SchemaProperty(document, "QueueEntryResponse", "sequenceId"))
      .Should().Contain("sequence this entry runs");
    Description(SchemaProperty(document, "QueueEntryResponse", "sequenceName"))
      .Should().Contain("null").And.Contain("no longer exists");
    Description(SchemaProperty(document, "QueueEntryResponse", "stale"))
      .Should().Contain("no longer exists");
  }

  [Fact]
  public async Task GetQueueOperationDescribesRoster() {
    var document = await ReadDocumentAsync().ConfigureAwait(false);

    Description(document.GetProperty("paths").GetProperty("/api/queues/{id}").GetProperty("get"))
      .Should().Contain("entries").And.Contain("roster").And.Contain("pauseKind");
  }

  [Theory]
  [InlineData("post")]
  [InlineData("put")]
  public async Task EntriesWriteOperationsPointToReadPath(string method) {
    var document = await ReadDocumentAsync().ConfigureAwait(false);

    Description(document.GetProperty("paths").GetProperty("/api/queues/{id}/entries").GetProperty(method))
      .Should().Contain("GET /api/queues/{id}").And.Contain("entries");
  }

  [Fact]
  public async Task GetQueueExampleShowsEntries() {
    var document = await ReadDocumentAsync().ConfigureAwait(false);

    document.GetProperty("paths").GetProperty("/api/queues/{id}").GetProperty("get")
      .GetProperty("responses").GetProperty("200").GetProperty("content").GetProperty("application/json")
      .GetProperty("example").GetProperty("entries").ValueKind.Should().Be(JsonValueKind.Array);
  }

  [Fact]
  public async Task MonitorOperationDoesNotClaimRoster() {
    var document = await ReadDocumentAsync().ConfigureAwait(false);

    (Description(document.GetProperty("paths").GetProperty("/api/queues/{id}/monitor").GetProperty("get")) ?? string.Empty)
      .Should().NotContain("roster");
  }
}
