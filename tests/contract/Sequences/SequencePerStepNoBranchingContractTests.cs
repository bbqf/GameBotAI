using System;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace GameBot.ContractTests.Sequences;

public sealed class SequencePerStepNoBranchingContractTests {
  private static WebApplicationFactory<Program> CreateFactory() {
    Environment.SetEnvironmentVariable("GAMEBOT_USE_ADB", "false");
    Environment.SetEnvironmentVariable("GAMEBOT_DYNAMIC_PORT", "true");
    Environment.SetEnvironmentVariable("GAMEBOT_AUTH_TOKEN", "test-token");
    return new WebApplicationFactory<Program>();
  }

  [Fact]
  public async Task CreateRejectsEntryStepAndLinksFields() {
    using var app = CreateFactory();
    var client = app.CreateClient();
    client.DefaultRequestHeaders.Add("Authorization", "Bearer test-token");

    var payload = new {
      name = "legacy-flow-reject",
      version = 1,
      entryStepId = "start",
      steps = new object[] {
        new {
          stepId = "start",
          action = new {
            type = "tap",
            parameters = new { x = 100, y = 100 }
          }
        }
      },
      links = new object[] {
        new {
          linkId = "l1",
          sourceStepId = "start",
          targetStepId = "start",
          branchType = "next"
        }
      }
    };

    var response = await client.PostAsJsonAsync("/api/sequences", payload).ConfigureAwait(false);

    response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    var content = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
    content.Should().Contain("entryStepId");
    content.Should().Contain("links");
  }

  [Fact]
  public async Task CreatedAndFetchedSequenceDoNotExposeBranchGraphFields() {
    using var app = CreateFactory();
    var client = app.CreateClient();
    client.DefaultRequestHeaders.Add("Authorization", "Bearer test-token");

    var createPayload = new {
      name = "linear-contract",
      version = 1,
      steps = new object[] {
        new {
          stepId = "step-1",
          action = new {
            type = "tap",
            parameters = new { x = 120, y = 840 }
          }
        }
      }
    };

    var createResponse = await client.PostAsJsonAsync("/api/sequences", createPayload).ConfigureAwait(false);
    createResponse.StatusCode.Should().Be(HttpStatusCode.Created);

    var created = await createResponse.Content.ReadFromJsonAsync<JsonElement>().ConfigureAwait(false);
    created.TryGetProperty("entryStepId", out _).Should().BeFalse();
    created.TryGetProperty("links", out _).Should().BeFalse();

    var sequenceId = created.GetProperty("id").GetString();
    sequenceId.Should().NotBeNullOrWhiteSpace();

    var getResponse = await client.GetAsync(new Uri($"/api/sequences/{sequenceId}", UriKind.Relative)).ConfigureAwait(false);
    getResponse.StatusCode.Should().Be(HttpStatusCode.OK);

    var fetched = await getResponse.Content.ReadFromJsonAsync<JsonElement>().ConfigureAwait(false);
    fetched.TryGetProperty("entryStepId", out _).Should().BeFalse();
    fetched.TryGetProperty("links", out _).Should().BeFalse();
  }
}
