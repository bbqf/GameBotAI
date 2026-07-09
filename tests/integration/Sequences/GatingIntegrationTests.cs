using System.Linq;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using GameBot.Domain.Commands;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace GameBot.IntegrationTests.Sequences {
  public class GatingIntegrationTests {
    // Gate steps cannot be authored through the /api/sequences create endpoint (its authoring
    // shape only accepts string command ids), so the sequences are seeded directly into the
    // repository. Previously these tests posted a domain-shaped payload whose steps were
    // silently dropped and then executed a non-existent sequence id — asserting nothing.
    private static async Task SeedAsync(IServiceProvider services, CommandSequence sequence) {
      await services.GetRequiredService<ISequenceRepository>().CreateAsync(sequence).ConfigureAwait(false);
    }

    [Fact]
    public async Task ExecuteSequenceFailsOnTimeoutGate() {
      TestEnvironment.PrepareCleanDataDir();
      Environment.SetEnvironmentVariable("GAMEBOT_AUTH_TOKEN", "test-token");
      using var app = new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program>();
      var client = app.CreateClient();
      client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", "test-token");

      var seq = new CommandSequence {
        Id = "g-int-1",
        Name = "gate-timeout",
      };
      seq.SetSteps(new[]
      {
                new SequenceStep { Order = 1, CommandId = "cmd-1", TimeoutMs = 200, Gate = new GateConfig { TargetId = "never", Condition = GateCondition.Present } }
            });
      await SeedAsync(app.Services, seq);

      using var empty = new StringContent("");
      var execResp = await client.PostAsync(new Uri($"/api/sequences/{seq.Id}/execute", UriKind.Relative), empty);
      execResp.StatusCode.Should().Be(HttpStatusCode.OK);
      var res = await execResp.Content.ReadFromJsonAsync<JsonElement>();
      // The "never" gate cannot pass; the 200ms timeout elapses and fails the run before the
      // command step is attempted.
      res.GetProperty("status").GetString().Should().Be("Failed");
    }

    [Fact]
    public async Task ExecuteSequencePassesGateAndProceedsToTheStep() {
      TestEnvironment.PrepareCleanDataDir();
      Environment.SetEnvironmentVariable("GAMEBOT_AUTH_TOKEN", "test-token");
      using var app = new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program>();
      var client = app.CreateClient();
      client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", "test-token");

      var seq = new CommandSequence {
        Id = "g-int-2",
        Name = "gate-pass",
      };
      seq.SetSteps(new[]
      {
                new SequenceStep { Order = 1, CommandId = "cmd-1", TimeoutMs = 500, Gate = new GateConfig { TargetId = "always", Condition = GateCondition.Present } }
            });
      await SeedAsync(app.Services, seq);

      using var empty2 = new StringContent("");
      var execResp = await client.PostAsync(new Uri($"/api/sequences/{seq.Id}/execute", UriKind.Relative), empty2);
      execResp.StatusCode.Should().Be(HttpStatusCode.OK);
      var res = await execResp.Content.ReadFromJsonAsync<JsonElement>();
      // The gate passed and execution reached the command step. "cmd-1" does not exist, and a
      // dangling command reference now fails the step loudly instead of fake-succeeding, so the
      // run fails at the command — not with a "Gating timeout reached" message.
      res.GetProperty("status").GetString().Should().Be("Failed");
      var messages = res.GetProperty("steps").EnumerateArray()
        .Select(s => s.TryGetProperty("message", out var m) ? m.GetString() : null)
        .ToList();
      messages.Should().Contain(m => m != null && m.Contains("cmd-1") && m.Contains("not found"));
    }
  }
}
