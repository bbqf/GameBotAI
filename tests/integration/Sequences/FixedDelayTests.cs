using System;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace GameBot.IntegrationTests.Sequences {
  public class FixedDelayTests {
    public FixedDelayTests() { }

    // Object-shaped step entries ({ order, commandId, delayMs }) have no stepType and no
    // primitiveAction. Before issue #242, the service dropped them silently and stored a sequence
    // with zero steps. Now the service reads them as per-step steps and rejects them with 400.
    [Fact]
    public async Task ObjectShapedStepsWithoutPrimitiveActionAreRejectedAndNothingIsStored() {
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
      Assert.Equal(HttpStatusCode.BadRequest, createResp.StatusCode);
      var body = await createResp.Content.ReadFromJsonAsync<JsonElement>();
      Assert.Contains(body.GetProperty("errors").EnumerateArray(), e => e.GetString()!.StartsWith("steps[0]", StringComparison.Ordinal));

      var list = await client.GetFromJsonAsync<JsonElement>(new Uri(client.BaseAddress!, "/api/sequences"));
      Assert.DoesNotContain(list.EnumerateArray(), s => s.GetProperty("name").GetString() == "it_fixed_delay");
    }
  }
}
