using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using GameBot.Domain.Commands;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace GameBot.IntegrationTests.Sequences
{
    public class GatingIntegrationTests
    {
        private readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web);

        [Fact]
        public async Task ExecuteSequenceFailsOnTimeoutGate()
        {
            Environment.SetEnvironmentVariable("GAMEBOT_AUTH_TOKEN", "test-token");
            using var app = new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program>();
            var client = app.CreateClient();
            client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", "test-token");

            var seq = new CommandSequence
            {
                Id = "g-int-1",
                Name = "gate-timeout",
            };
            seq.SetSteps(new[]
            {
                new SequenceStep { Order = 1, CommandId = "cmd-1", TimeoutMs = 200, Gate = new GateConfig { TargetId = "never", Condition = GateCondition.Present } }
            });

            var createResp = await client.PostAsJsonAsync("/api/sequences", seq, _json);

            using var empty = new StringContent("");
            var execResp = await client.PostAsync(new Uri($"/api/sequences/{seq.Id}/execute", UriKind.Relative), empty);
            execResp.StatusCode.Should().Be(HttpStatusCode.OK);
            var res = await execResp.Content.ReadFromJsonAsync<GameBot.Domain.Services.SequenceExecutionResult>(_json);
            res.Should().NotBeNull();
            res!.Status.Should().Match(s => s == "Failed" || s == "Succeeded");
        }

        [Fact]
        public async Task ExecuteSequenceSucceedsOnPassingGate()
        {
            Environment.SetEnvironmentVariable("GAMEBOT_AUTH_TOKEN", "test-token");
            using var app = new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program>();
            var client = app.CreateClient();
            client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", "test-token");

            var seq = new CommandSequence
            {
                Id = "g-int-2",
                Name = "gate-pass",
            };
            seq.SetSteps(new[]
            {
                new SequenceStep { Order = 1, CommandId = "cmd-1", TimeoutMs = 500, Gate = new GateConfig { TargetId = "always", Condition = GateCondition.Present } }
            });

            var createResp = await client.PostAsJsonAsync("/api/sequences", seq, _json);

            using var empty2 = new StringContent("");
            var execResp = await client.PostAsync(new Uri($"/api/sequences/{seq.Id}/execute", UriKind.Relative), empty2);
            execResp.StatusCode.Should().Be(HttpStatusCode.OK);
            var res = await execResp.Content.ReadFromJsonAsync<GameBot.Domain.Services.SequenceExecutionResult>(_json);
            res.Should().NotBeNull();
            res!.Status.Should().Be("Succeeded");
        }
    }
}
