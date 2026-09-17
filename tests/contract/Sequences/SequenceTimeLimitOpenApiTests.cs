using System;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace GameBot.ContractTests.Sequences;

/// <summary>
/// Feature 094 (FR-009, issue #182): the per-sequence time bound was "established by observing runs get
/// guillotined, not from the OpenAPI document". The published document must state the bound's default
/// and maximum, the effective read-out, and the execution-log fields that report a time-limit cancellation.
/// </summary>
public sealed class SequenceTimeLimitOpenApiTests {
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

  private static JsonElement Property(JsonElement document, string schema, string property)
    => document.GetProperty("components").GetProperty("schemas").GetProperty(schema)
      .GetProperty("properties").GetProperty(property);

  [Theory]
  [InlineData("SequenceUpsertRequest")]
  [InlineData("SequencePatchContract")]
  public async Task WatchdogTimeoutDocumentsItsBoundsAndDefault(string schema) {
    var document = await ReadDocumentAsync().ConfigureAwait(false);

    var watchdog = Property(document, schema, "watchdogTimeoutMs");

    watchdog.GetProperty("minimum").GetInt32().Should().Be(1);
    watchdog.GetProperty("maximum").GetInt32().Should().Be(1800000);
    watchdog.GetProperty("description").GetString().Should().Contain("240000");
  }

  [Fact]
  public async Task GetSequenceDocumentsTheEffectiveBound() {
    var document = await ReadDocumentAsync().ConfigureAwait(false);

    var description = document.GetProperty("paths").GetProperty("/api/sequences/{sequenceId}")
      .GetProperty("get").GetProperty("description").GetString();

    description.Should().Contain("effectiveWatchdogTimeoutMs").And.Contain("240000");
  }

  [Fact]
  public async Task ExecutionLogEntrySchemaDocumentsTheCancellationFields() {
    var document = await ReadDocumentAsync().ConfigureAwait(false);

    Property(document, "ExecutionLogEntryDto", "cancellationReason").GetProperty("description").GetString()
      .Should().Contain("sequence_time_limit");
    Property(document, "ExecutionLogEntryDto", "timeLimitMs").GetProperty("description").GetString()
      .Should().NotBeNullOrWhiteSpace();
  }

  [Fact]
  public async Task SubtreeOperationDocumentsTheCancellationFieldsOnNodes() {
    var document = await ReadDocumentAsync().ConfigureAwait(false);

    var description = document.GetProperty("paths").GetProperty("/api/execution-logs/{id}/subtree")
      .GetProperty("get").GetProperty("description").GetString();

    description.Should().Contain("cancellationReason").And.Contain("timeLimitMs").And.Contain("sequence_time_limit");
  }
}
