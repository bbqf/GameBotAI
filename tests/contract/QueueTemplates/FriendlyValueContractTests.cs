using System;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using GameBot.Domain.Commands;
using GameBot.Domain.Parameters;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace GameBot.ContractTests.QueueTemplates;

/// <summary>Feature 168 (FR-012): a template value that does not fit its declaration is a warning, and the save works.</summary>
public sealed class FriendlyValueContractTests {
  private static WebApplicationFactory<Program> CreateFactory() {
    Environment.SetEnvironmentVariable("GAMEBOT_USE_ADB", "false");
    Environment.SetEnvironmentVariable("GAMEBOT_DYNAMIC_PORT", "true");
    Environment.SetEnvironmentVariable("GAMEBOT_AUTH_TOKEN", "test-token");
    return new WebApplicationFactory<Program>();
  }

  private static async Task<string> SeedSequenceAsync(WebApplicationFactory<Program> app) {
    var sequence = new CommandSequence { Id = $"seq-{Guid.NewGuid():N}", Name = "Friendly" };
    sequence.Parameters.Add(new ParameterDeclaration { Name = "speed", Type = ParameterValueType.Number, Min = 1, Max = 9 });
    var action = new SequenceActionPayload { Type = "tap" };
    action.Parameters["x"] = 1;
    action.Parameters["y"] = 2;
    sequence.SetSteps(new[] {
      new SequenceStep { Order = 0, StepId = "tap", StepType = SequenceStepType.Action, Action = action }
    });
    await app.Services.GetRequiredService<ISequenceRepository>().CreateAsync(sequence).ConfigureAwait(false);
    return sequence.Id;
  }

  private static async Task<(HttpStatusCode Status, JsonElement Body)> SaveAsync(
      WebApplicationFactory<Program> app, string sequenceId, string value) {
    var client = app.CreateClient();
    client.DefaultRequestHeaders.Add("Authorization", "Bearer test-token");
    var response = await client.PostAsJsonAsync("/api/queue-templates", new {
      name = $"tpl-{Guid.NewGuid():N}",
      overwrite = true,
      entries = new object[] {
        new { sequenceId, scheduleType = "OncePerRun", enabled = true, parameterValues = new object[] { new { name = "speed", value } } }
      }
    }).ConfigureAwait(false);
    var text = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
    return (response.StatusCode, JsonDocument.Parse(text).RootElement);
  }

  [Fact]
  public async Task ValueOutOfRangeSavesWithAWarning() {
    using var app = CreateFactory();
    var sequenceId = await SeedSequenceAsync(app).ConfigureAwait(false);

    var (status, body) = await SaveAsync(app, sequenceId, "12").ConfigureAwait(false);

    status.Should().BeOneOf(HttpStatusCode.Created, HttpStatusCode.OK);
    var warning = body.GetProperty("warnings").EnumerateArray().Single();
    warning.GetProperty("code").GetString().Should().Be("invalid_parameter_value");
    warning.GetProperty("parameterName").GetString().Should().Be("speed");
    warning.GetProperty("message").GetString().Should().Contain("Enter a number");
  }

  [Fact]
  public async Task ValueInRangeSavesWithoutAWarning() {
    using var app = CreateFactory();
    var sequenceId = await SeedSequenceAsync(app).ConfigureAwait(false);

    var (status, body) = await SaveAsync(app, sequenceId, "5").ConfigureAwait(false);

    status.Should().BeOneOf(HttpStatusCode.Created, HttpStatusCode.OK);
    (body.TryGetProperty("warnings", out var warnings) && warnings.ValueKind == JsonValueKind.Array
        && warnings.GetArrayLength() > 0).Should().BeFalse();
  }
}
