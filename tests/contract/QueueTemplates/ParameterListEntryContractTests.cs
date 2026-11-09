#pragma warning disable CA1861 // Test data arrays are small and used one time.
using System;
using System.Linq;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace GameBot.ContractTests.QueueTemplates;

/// <summary>Feature 179: list values on queue-template entries.</summary>
public sealed class ParameterListEntryContractTests {
  private static WebApplicationFactory<Program> CreateFactory() {
    Environment.SetEnvironmentVariable("GAMEBOT_USE_ADB", "false");
    Environment.SetEnvironmentVariable("GAMEBOT_DYNAMIC_PORT", "true");
    Environment.SetEnvironmentVariable("GAMEBOT_AUTH_TOKEN", "test-token");
    return new WebApplicationFactory<Program>();
  }

  private static System.Net.Http.HttpClient AuthedClient(WebApplicationFactory<Program> app) {
    var client = app.CreateClient();
    client.DefaultRequestHeaders.Add("Authorization", "Bearer test-token");
    return client;
  }

  private static async Task<string> CreateSequenceAsync(
      System.Net.Http.HttpClient client, bool required = false, int? minItems = null, int? maxItems = null) {
    var response = await client.PostAsJsonAsync("/api/sequences", new {
      name = $"list-seq-{Guid.NewGuid():N}",
      version = 1,
      parameters = new object[] {
        new {
          name = "quests", type = "text", kind = "image", multiple = true, required, minItems, maxItems,
          allowedImages = new[] { "q-a", "q-b", "q-c" }
        }
      },
      steps = new object[] {
        new { stepId = "s1", stepType = "Action", primitiveAction = new { type = "tap", schemaVersion = "v1", payload = new { x = 1, y = 2 } } }
      }
    }).ConfigureAwait(false);
    var text = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
    response.StatusCode.Should().Be(HttpStatusCode.Created, text);
    return JsonDocument.Parse(text).RootElement.GetProperty("id").GetString()!;
  }

  private static async Task<(HttpStatusCode Status, JsonElement Body)> SaveTemplateAsync(
      System.Net.Http.HttpClient client, string sequenceId, object parameterValue) {
    var response = await client.PostAsJsonAsync("/api/queue-templates", new {
      name = $"list-tpl-{Guid.NewGuid():N}",
      overwrite = true,
      entries = new object[] { new { sequenceId, scheduleType = "OncePerRun", parameterValues = new[] { parameterValue } } }
    }).ConfigureAwait(false);
    var text = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
    return (response.StatusCode, JsonDocument.Parse(text).RootElement.Clone());
  }

  private static JsonElement FirstValue(JsonElement body) =>
      body.GetProperty("entries").EnumerateArray().Single().GetProperty("parameterValues").EnumerateArray().Single();

  [Fact]
  public async Task ValuesRoundTripInOrderWithDuplicatesRemoved() {
    using var app = CreateFactory();
    var client = AuthedClient(app);
    var sequenceId = await CreateSequenceAsync(client).ConfigureAwait(false);

    var (status, body) = await SaveTemplateAsync(client, sequenceId, new {
      name = "quests", values = new[] { "q-b", "q-a", "q-b" }
    }).ConfigureAwait(false);

    status.Should().BeOneOf(HttpStatusCode.Created, HttpStatusCode.OK);
    var value = FirstValue(body);
    value.GetProperty("values").EnumerateArray().Select(e => e.GetString()).Should().Equal("q-b", "q-a");
    value.GetProperty("value").GetString().Should().Be("q-b|q-a");
  }

  [Fact]
  public async Task WhenValueAndValuesAreSentValuesWins() {
    using var app = CreateFactory();
    var client = AuthedClient(app);
    var sequenceId = await CreateSequenceAsync(client).ConfigureAwait(false);

    var (status, body) = await SaveTemplateAsync(client, sequenceId, new {
      name = "quests", value = "q-c", values = new[] { "q-a" }
    }).ConfigureAwait(false);

    status.Should().BeOneOf(HttpStatusCode.Created, HttpStatusCode.OK);
    FirstValue(body).GetProperty("value").GetString().Should().Be("q-a");
  }

  [Fact]
  public async Task ScalarValueIsReturnedAsSentAndHasNoValuesMember() {
    using var app = CreateFactory();
    var client = AuthedClient(app);
    var sequenceId = await CreateSequenceAsync(client).ConfigureAwait(false);

    var (status, body) = await SaveTemplateAsync(client, sequenceId, new { name = "quests", value = "q-a" })
        .ConfigureAwait(false);

    status.Should().BeOneOf(HttpStatusCode.Created, HttpStatusCode.OK);
    var value = FirstValue(body);
    value.GetProperty("value").GetString().Should().Be("q-a");
    value.TryGetProperty("values", out _).Should().BeFalse();
  }

  [Fact]
  public async Task EmptyListOfARequiredParameterIsRefused() {
    using var app = CreateFactory();
    var client = AuthedClient(app);
    var sequenceId = await CreateSequenceAsync(client, required: true).ConfigureAwait(false);

    var (status, body) = await SaveTemplateAsync(client, sequenceId, new { name = "quests", values = Array.Empty<string>() })
        .ConfigureAwait(false);

    status.Should().Be(HttpStatusCode.BadRequest);
    body.GetProperty("error").GetString().Should().Be("empty_parameter_list");
  }

  [Fact]
  public async Task ListAboveTheMaximumIsRefused() {
    using var app = CreateFactory();
    var client = AuthedClient(app);
    var sequenceId = await CreateSequenceAsync(client, maxItems: 1).ConfigureAwait(false);

    var (status, body) = await SaveTemplateAsync(client, sequenceId, new { name = "quests", values = new[] { "q-a", "q-b" } })
        .ConfigureAwait(false);

    status.Should().Be(HttpStatusCode.BadRequest);
    body.GetProperty("error").GetString().Should().Be("parameter_list_out_of_limits");
  }

  [Fact]
  public async Task ListBelowTheMinimumIsRefused() {
    using var app = CreateFactory();
    var client = AuthedClient(app);
    var sequenceId = await CreateSequenceAsync(client, minItems: 2).ConfigureAwait(false);

    var (status, body) = await SaveTemplateAsync(client, sequenceId, new { name = "quests", values = new[] { "q-a" } })
        .ConfigureAwait(false);

    status.Should().Be(HttpStatusCode.BadRequest);
    body.GetProperty("error").GetString().Should().Be("parameter_list_out_of_limits");
  }

  [Fact]
  public async Task NotAllowedItemGivesAWarningAndTheSaveWorks() {
    using var app = CreateFactory();
    var client = AuthedClient(app);
    var sequenceId = await CreateSequenceAsync(client).ConfigureAwait(false);

    var (status, body) = await SaveTemplateAsync(client, sequenceId, new { name = "quests", values = new[] { "q-a", "zzz-179" } })
        .ConfigureAwait(false);

    status.Should().BeOneOf(HttpStatusCode.Created, HttpStatusCode.OK);
    body.GetProperty("warnings").EnumerateArray()
        .Should().Contain(w => w.GetProperty("code").GetString() == "invalid_parameter_value");
  }
}
