using System;
using System.Linq;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace GameBot.IntegrationTests.Sequences;

public sealed class LegacySequenceCompatibilityIntegrationTests {
  [Fact]
  public async Task LegacyLinearSequenceCreateGetAndExecuteRemainCompatible() {
    TestEnvironment.PrepareCleanDataDir();
    Environment.SetEnvironmentVariable("GAMEBOT_AUTH_TOKEN", "test-token");

    using var app = new WebApplicationFactory<Program>();
    var client = app.CreateClient();
    client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "test-token");

    var createPayload = new {
      name = "legacy-linear-sequence",
      steps = new[] {
        "cmd-legacy-a",
        "cmd-legacy-b"
      }
    };

    var createResponse = await client.PostAsJsonAsync("/api/sequences", createPayload).ConfigureAwait(false);
    createResponse.StatusCode.Should().Be(HttpStatusCode.Created);
    var created = await createResponse.Content.ReadFromJsonAsync<JsonElement>().ConfigureAwait(false);
    var sequenceId = created.GetProperty("id").GetString();
    sequenceId.Should().NotBeNullOrWhiteSpace();

    var getResponse = await client.GetAsync(new Uri($"/api/sequences/{sequenceId}", UriKind.Relative)).ConfigureAwait(false);
    getResponse.StatusCode.Should().Be(HttpStatusCode.OK);
    var fetched = await getResponse.Content.ReadFromJsonAsync<JsonElement>().ConfigureAwait(false);

    fetched.GetProperty("id").GetString().Should().Be(sequenceId);
    fetched.TryGetProperty("entryStepId", out _).Should().BeFalse("legacy linear sequences should preserve original payload shape");
    var steps = fetched.GetProperty("steps").EnumerateArray().Select(x => x.GetString()).ToArray();
    steps.Should().HaveCount(2);
    steps[0].Should().Be("cmd-legacy-a");
    steps[1].Should().Be("cmd-legacy-b");

    // Executing still works through the legacy shape, but the referenced commands do not
    // exist — a dangling command reference now fails the run loudly (at the first step)
    // instead of fake-reporting every step as executed.
    var executeResponse = await client.PostAsync(new Uri($"/api/sequences/{sequenceId}/execute", UriKind.Relative), null).ConfigureAwait(false);
    executeResponse.StatusCode.Should().Be(HttpStatusCode.OK);
    var execution = await executeResponse.Content.ReadFromJsonAsync<JsonElement>().ConfigureAwait(false);
    execution.GetProperty("status").GetString().Should().Be("Failed");
    execution.GetProperty("steps").GetArrayLength().Should().Be(1);
    execution.GetProperty("steps")[0].GetProperty("message").GetString().Should().Contain("cmd-legacy-a");
  }
}
