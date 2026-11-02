using System;
using System.Linq;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

#pragma warning disable CA2007

namespace GameBot.ContractTests.Sequences;

/// <summary>
/// Feature 174: a WaitForStill step with a wrong payload is a 400 with a clear error, never a 500. A good step is
/// saved. The published text names the two outcomes.
/// </summary>
public sealed class WaitForStillContractTests {
  private static WebApplicationFactory<Program> CreateFactory() {
    Environment.SetEnvironmentVariable("GAMEBOT_USE_ADB", "false");
    Environment.SetEnvironmentVariable("GAMEBOT_DYNAMIC_PORT", "true");
    Environment.SetEnvironmentVariable("GAMEBOT_AUTH_TOKEN", "test-token");
    return new WebApplicationFactory<Program>();
  }

  private static System.Net.Http.HttpClient Authed(WebApplicationFactory<Program> app) {
    var client = app.CreateClient();
    client.DefaultRequestHeaders.Add("Authorization", "Bearer test-token");
    return client;
  }

  private static async Task<(HttpStatusCode Status, string Body)> SaveAsync(System.Net.Http.HttpClient client, object payload, string type = "WaitForStill") {
    var response = await client.PostAsJsonAsync("/api/sequences", new {
      name = "wait-still-contract-" + Guid.NewGuid().ToString("N"),
      version = 1,
      steps = new object[] {
        new {
          stepId = "wait-pan",
          stepType = "Action",
          primitiveAction = new { type, schemaVersion = "1", payload }
        }
      }
    }).ConfigureAwait(false);
    return (response.StatusCode, await response.Content.ReadAsStringAsync().ConfigureAwait(false));
  }

  [Fact]
  public async Task AnEmptyPayloadAndAFullPayloadAreSaved() {
    using var app = CreateFactory();
    var client = Authed(app);

    (await SaveAsync(client, new { })).Status.Should().Be(HttpStatusCode.Created);
    (await SaveAsync(client, new {
      region = new { x = 0, y = 120, width = 720, height = 900 }, stableMs = 300, maxDiff = 0.01, timeoutMs = 5000
    }, "waitforstill")).Status.Should().Be(HttpStatusCode.Created);
  }

  [Fact]
  public async Task ABadPayloadIsRejectedWithAClearError() {
    using var app = CreateFactory();
    var client = Authed(app);

    var (status, body) = await SaveAsync(client, new { stableMs = 6000, timeoutMs = 5000, maxDiff = 2, bogus = 1 });

    status.Should().Be(HttpStatusCode.BadRequest);
    body.Should().Contain("Step 'wait-pan' WaitForStill maxDiff must be a number from 0 to 1");
    body.Should().Contain("Step 'wait-pan' WaitForStill payload field 'bogus' is not supported");
  }

  [Fact]
  public async Task AStableTimeAboveTheTimeLimitIsRejected() {
    using var app = CreateFactory();
    var client = Authed(app);

    var (status, body) = await SaveAsync(client, new { stableMs = 6000 });

    status.Should().Be(HttpStatusCode.BadRequest);
    body.Should().Contain("stableMs must not be greater than timeoutMs, so that the step can end with still");
  }

  [Fact]
  public async Task ABadRegionIsRejected() {
    using var app = CreateFactory();
    var client = Authed(app);

    var (status, body) = await SaveAsync(client, new { region = new { x = -1, y = 0, width = 0, height = 10 } });

    status.Should().Be(HttpStatusCode.BadRequest);
    body.Should().Contain("region.x must be 0 or more").And.Contain("region.width must be greater than 0");
  }

  [Fact]
  public async Task TheExpectedStateTextNamesTheTwoOutcomes() {
    using var app = CreateFactory();
    var client = app.CreateClient();
    var text = await client.GetStringAsync(new Uri("/swagger/v1/swagger.json", UriKind.Relative)).ConfigureAwait(false);
    using var document = JsonDocument.Parse(text);

    var all = document.RootElement.GetProperty("components").GetProperty("schemas").EnumerateObject()
      .SelectMany(s => s.Value.TryGetProperty("properties", out var p) ? p.EnumerateObject() : Enumerable.Empty<JsonProperty>())
      .Where(p => p.Name == "expectedState" && p.Value.TryGetProperty("description", out _))
      .Select(p => p.Value.GetProperty("description").GetString()!)
      .ToList();

    all.Should().Contain(d => d.Contains("still", StringComparison.Ordinal) && d.Contains("timeout_elapsed", StringComparison.Ordinal));
  }
}
