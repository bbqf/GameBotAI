using System;
using System.Net.Http.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace GameBot.IntegrationTests.Sequences {
  public class FixedDelayTests {
    public FixedDelayTests() { }

    // The /api/sequences authoring shape only accepts string command ids; object-shaped step
    // entries ({ order, commandId, delayMs }) are silently dropped, so the created sequence has
    // no steps and executing it succeeds trivially. This test pins that contract. (It used to
    // assert "Succeeded" believing the steps ran with delays — they never existed.)
    [Fact]
    public async Task ObjectShapedStepsAreDroppedByAuthoringShapeAndExecuteSucceedsWithNoSteps() {
      Environment.SetEnvironmentVariable("GAMEBOT_AUTH_TOKEN", "test-token");
      Environment.SetEnvironmentVariable("GAMEBOT_DYNAMIC_PORT", "true");
      using var app = new WebApplicationFactory<Program>();
      var client = app.CreateClient();
      client.DefaultRequestHeaders.Add("Authorization", "Bearer test-token");

      var seq = new {
        name = "it_fixed_delay",
        steps = new object[]
          {
                    new { order = 1, commandId = "cmd-A", delayMs = 300 },
                    new { order = 2, commandId = "cmd-B", delayMs = 400 }
          }
      };

      var createResp = await client.PostAsJsonAsync("/api/sequences", seq);
      createResp.EnsureSuccessStatusCode();
      var created = await createResp.Content.ReadFromJsonAsync<SequenceDto>();
      Assert.NotNull(created);

      var execUri = new Uri(client.BaseAddress!, $"/api/sequences/{created!.id}/execute");
      var execResp = await client.PostAsync(execUri, content: null);
      execResp.EnsureSuccessStatusCode();
      var res = await execResp.Content.ReadFromJsonAsync<ExecuteResultDto>();

      Assert.NotNull(res);
      Assert.Equal("Succeeded", res!.status);
      Assert.Equal(created!.id, res.sequenceId);
      Assert.NotNull(res.steps);
      Assert.Empty(res.steps);
    }

    private sealed class SequenceDto {
      public string id { get; set; } = string.Empty;
    }

    private sealed class ExecuteResultDto {
      public string sequenceId { get; set; } = string.Empty;
      public string status { get; set; } = string.Empty;
      public StepDto[] steps { get; set; } = Array.Empty<StepDto>();
    }

    private sealed class StepDto {
      public int order { get; set; }
      public string commandId { get; set; } = string.Empty;
      public int appliedDelayMs { get; set; }
    }
  }
}
