using System;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using GameBot.Domain.Commands;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace GameBot.IntegrationTests.Sequences;

public sealed class SequenceMissingCommandReferenceIntegrationTests {
  private static WebApplicationFactory<Program> CreateFactory() {
    Environment.SetEnvironmentVariable("GAMEBOT_USE_ADB", "false");
    Environment.SetEnvironmentVariable("GAMEBOT_DYNAMIC_PORT", "true");
    Environment.SetEnvironmentVariable("GAMEBOT_AUTH_TOKEN", "test-token");
    TestEnvironment.PrepareCleanDataDir();
    return new WebApplicationFactory<Program>();
  }

  [Fact]
  public async Task GetAndResavePreserveUnresolvedCommandSnapshotAfterCommandDeletion() {
    using var app = CreateFactory();
    var client = app.CreateClient();
    client.DefaultRequestHeaders.Add("Authorization", "Bearer test-token");

    var commandRepository = app.Services.GetRequiredService<ICommandRepository>();
    await commandRepository.AddAsync(new Command {
      Id = "missing-cmd",
      Name = "Deleted Command"
    }).ConfigureAwait(false);

    var createPayload = new {
      name = "missing-command-sequence",
      version = 1,
      steps = new object[] {
        new {
          stepId = "step-1",
          label = "Deleted command step",
          stepType = "Action",
          primitiveAction = new {
            type = "command",
            schemaVersion = "v1",
            payload = new { commandId = "missing-cmd" }
          }
        }
      }
    };

    var createResponse = await client.PostAsJsonAsync("/api/sequences", createPayload).ConfigureAwait(false);
    createResponse.StatusCode.Should().Be(HttpStatusCode.Created);
    var created = await createResponse.Content.ReadFromJsonAsync<JsonElement>().ConfigureAwait(false);
    var sequenceId = created.GetProperty("id").GetString();
    sequenceId.Should().NotBeNullOrWhiteSpace();

    await commandRepository.DeleteAsync("missing-cmd").ConfigureAwait(false);

    var getResponse = await client.GetAsync(new Uri($"/api/sequences/{sequenceId}", UriKind.Relative)).ConfigureAwait(false);
    getResponse.StatusCode.Should().Be(HttpStatusCode.OK);
    var fetched = await getResponse.Content.ReadFromJsonAsync<JsonElement>().ConfigureAwait(false);

    var fetchedStep = fetched.GetProperty("steps")[0];
    fetchedStep.GetProperty("primitiveAction").GetProperty("payload").GetProperty("commandId").GetString().Should().Be("missing-cmd");
    fetchedStep.GetProperty("commandReference").GetProperty("commandId").GetString().Should().Be("missing-cmd");
    fetchedStep.GetProperty("commandReference").GetProperty("commandName").GetString().Should().Be("Deleted Command");
    fetchedStep.GetProperty("commandReference").GetProperty("isResolved").GetBoolean().Should().BeFalse();

    var patchPayload = new {
      name = fetched.GetProperty("name").GetString(),
      version = fetched.GetProperty("version").GetInt32(),
      steps = new object[] {
        new {
          stepId = fetchedStep.GetProperty("stepId").GetString(),
          label = fetchedStep.GetProperty("label").GetString(),
          stepType = fetchedStep.GetProperty("stepType").GetString(),
          primitiveAction = new {
            type = fetchedStep.GetProperty("primitiveAction").GetProperty("type").GetString(),
            schemaVersion = fetchedStep.GetProperty("primitiveAction").GetProperty("schemaVersion").GetString(),
            payload = new {
              commandId = fetchedStep.GetProperty("primitiveAction").GetProperty("payload").GetProperty("commandId").GetString()
            }
          },
          commandReference = new {
            commandId = fetchedStep.GetProperty("commandReference").GetProperty("commandId").GetString(),
            commandName = fetchedStep.GetProperty("commandReference").GetProperty("commandName").GetString(),
            isResolved = fetchedStep.GetProperty("commandReference").GetProperty("isResolved").GetBoolean()
          }
        }
      }
    };

    var patchResponse = await client.PatchAsJsonAsync($"/api/sequences/{sequenceId}", patchPayload).ConfigureAwait(false);
    patchResponse.StatusCode.Should().Be(HttpStatusCode.OK);

    var reloadResponse = await client.GetAsync(new Uri($"/api/sequences/{sequenceId}", UriKind.Relative)).ConfigureAwait(false);
    reloadResponse.StatusCode.Should().Be(HttpStatusCode.OK);
    var reloaded = await reloadResponse.Content.ReadFromJsonAsync<JsonElement>().ConfigureAwait(false);

    var reloadedStep = reloaded.GetProperty("steps")[0];
    reloadedStep.GetProperty("primitiveAction").GetProperty("payload").GetProperty("commandId").GetString().Should().Be("missing-cmd");
    reloadedStep.GetProperty("commandReference").GetProperty("commandName").GetString().Should().Be("Deleted Command");
    reloadedStep.GetProperty("commandReference").GetProperty("isResolved").GetBoolean().Should().BeFalse();
  }
}