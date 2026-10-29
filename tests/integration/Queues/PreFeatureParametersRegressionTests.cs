using System;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using GameBot.Domain.Commands;
using GameBot.Domain.Parameters;
using GameBot.Service.Services.ExecutionLog;
using GameBot.Service.Services.QueueExecution;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using static GameBot.IntegrationTests.Queues.FriendlyRunHelpers;

namespace GameBot.IntegrationTests.Queues;

/// <summary>
/// Feature 168 (SC-005, FR-018, FR-019): data saved before the feature loads and runs as before, and a
/// required parameter follows the feature 078 rule.
/// </summary>
[Collection("ConfigIsolation")]
public sealed class PreFeatureParametersRegressionTests {
  private static readonly JsonSerializerOptions WebOptions = new(JsonSerializerDefaults.Web);

  public PreFeatureParametersRegressionTests() {
    Environment.SetEnvironmentVariable("GAMEBOT_USE_ADB", "false");
    Environment.SetEnvironmentVariable("GAMEBOT_AUTH_TOKEN", "test-token");
    TestEnvironment.PrepareCleanDataDir();
  }

  [Fact]
  public void PreFeatureDeclarationJsonLoadsAsPlainTextAndNumberInputs() {
    const string json = """
      [ { "name": "a", "type": 0, "default": "x", "required": true, "description": "d" },
        { "name": "n", "type": 1, "default": "3", "required": false } ]
      """;

    var declarations = JsonSerializer.Deserialize<ParameterDeclaration[]>(json, WebOptions)!;

    declarations[0].EffectiveKind.Should().Be(ParameterInputKind.Text);
    declarations[1].EffectiveKind.Should().Be(ParameterInputKind.Number);
    declarations[1].Pattern.Should().BeNull();
  }

  [Fact]
  public async Task PlainDeclarationAndEntryValueRunWithTheSameResolvedValue() {
    using var app = new WebApplicationFactory<Program>();
    _ = app.CreateClient();
    var services = app.Services;
    await SeedAsync(
        services,
        KeyCommand("c-pre", "{{keyName}}", new ParameterDeclaration { Name = "keyName", Description = "old" }),
        SequenceInvoking("s-pre", "c-pre"),
        "q-pre",
        entry => entry.ParameterValues.Add(new ParameterBinding { Name = "keyName", Value = "KEYCODE_HOME" }))
      .ConfigureAwait(false);

    await RunToCompletionAsync(services.GetRequiredService<IQueueExecutionService>(), "q-pre").ConfigureAwait(false);

    var (text, data) = await ParameterLogAsync(services.GetRequiredService<IExecutionLogService>()).ConfigureAwait(false);
    text.Should().Contain("keyName=KEYCODE_HOME");
    data.Should().NotContain("label", "an unfriendly parameter has no option label");
  }

  [Fact]
  public async Task RequiredChoiceWithNoValueAndNoDefaultRefusesTheQueueStart() {
    using var app = new WebApplicationFactory<Program>();
    var client = app.CreateClient();
    client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "test-token");
    var services = app.Services;
    await SeedAsync(
        services,
        KeyCommand("c-req168", "{{mode}}", new ParameterDeclaration {
          Name = "mode", Required = true, Kind = ParameterInputKind.Choice,
          Options = new[] { new ParameterOption { Label = "Home", Value = "KEYCODE_HOME" } }
        }),
        SequenceInvoking("s-req168", "c-req168"),
        "q-req168")
      .ConfigureAwait(false);

    var response = await client.PostAsync(new Uri("/api/queues/q-req168/start", UriKind.Relative), content: null)
        .ConfigureAwait(false);

    response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    (await response.Content.ReadAsStringAsync().ConfigureAwait(false)).Should().Contain("missing_required_parameters");
  }

  [Fact]
  public async Task StaleChoiceValueDoesNotStopTheQueueStart() {
    // FR-016: a saved value that is not an option any more is a warning at save, never a refusal at start.
    using var app = new WebApplicationFactory<Program>();
    var client = app.CreateClient();
    client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "test-token");
    var services = app.Services;
    await SeedAsync(
        services,
        KeyCommand("c-stale168", "{{mode}}", new ParameterDeclaration {
          Name = "mode", Required = true, Kind = ParameterInputKind.Choice,
          Options = new[] { new ParameterOption { Label = "Home", Value = "KEYCODE_HOME" } }
        }),
        SequenceInvoking("s-stale168", "c-stale168"),
        "q-stale168",
        entry => entry.ParameterValues.Add(new ParameterBinding { Name = "mode", Value = "KEYCODE_REMOVED" }))
      .ConfigureAwait(false);

    var response = await client.PostAsync(new Uri("/api/queues/q-stale168/start", UriKind.Relative), content: null)
        .ConfigureAwait(false);

    response.StatusCode.Should().Be(HttpStatusCode.OK);
  }
}
