using System;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace GameBot.ContractTests.Sequences;

/// <summary>
/// Feature 170: the OpenAPI document describes <c>onTimeLimit</c>, its allowed values and its default, and
/// the two cases of <c>cancellationReason</c>.
/// </summary>
public sealed class SequenceOnTimeLimitOpenApiTests {
  private static async Task<JsonElement> ReadDocumentAsync() {
    Environment.SetEnvironmentVariable("GAMEBOT_USE_ADB", "false");
    Environment.SetEnvironmentVariable("GAMEBOT_DYNAMIC_PORT", "true");
    Environment.SetEnvironmentVariable("GAMEBOT_AUTH_TOKEN", "test-token");
    using var app = new WebApplicationFactory<Program>();
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
  [InlineData("SequenceRequestSchema")]
  [InlineData("SequenceResponseSchema")]
  public async Task TheSchemasDescribeTheOptionTheValuesAndTheDefault(string schema) {
    var document = await ReadDocumentAsync().ConfigureAwait(false);

    var description = Property(document, schema, "onTimeLimit").GetProperty("description").GetString();

    description.Should().Contain("'fail'").And.Contain("'end'").And.Contain("default");
  }

  [Fact]
  public async Task TheCancellationReasonStatesBothCases() {
    var document = await ReadDocumentAsync().ConfigureAwait(false);

    var description = Property(document, "ExecutionLogEntryDto", "cancellationReason").GetProperty("description").GetString();

    description.Should().Contain("sequence_time_limit").And.Contain("'failure'").And.Contain("'success'").And.Contain("onTimeLimit");
  }
}
