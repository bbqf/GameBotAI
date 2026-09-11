using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace GameBot.IntegrationTests;

public class SessionInputTests {
  public SessionInputTests() {
    Environment.SetEnvironmentVariable("GAMEBOT_USE_ADB", "false");
    Environment.SetEnvironmentVariable("GAMEBOT_DYNAMIC_PORT", "true");
    TestEnvironment.PrepareCleanDataDir();
  }

  [Fact]
  public async Task PostingInputsAcceptsActions() {
    Environment.SetEnvironmentVariable("GAMEBOT_AUTH_TOKEN", "test-token");
    using var app = new WebApplicationFactory<Program>();
    var client = app.CreateClient();
    client.DefaultRequestHeaders.Add("Authorization", "Bearer test-token");

    // Check devices and skip if none to avoid flakiness without ADB
    // Force stub mode for inputs test to avoid external ADB process hangs
    // Do NOT request adbSerial so session runs without ADB even if devices exist
    var createResp = await client.PostAsJsonAsync(new Uri("/api/sessions", UriKind.Relative), new { gameId = "test-game" }).ConfigureAwait(true);
    var created = await createResp.Content.ReadFromJsonAsync<Dictionary<string, object>>().ConfigureAwait(true);
    var id = created!["id"].ToString();

    var actions = new {
      actions = new[]
    {
            new { type = "key", args = new Dictionary<string, object>{{"key", "ESCAPE"}} },
            new { type = "key", args = new Dictionary<string, object>{{"keyCode", 29}} }, // 'A'
            new { type = "tap", args = new Dictionary<string, object>{{"x", 10},{"y", 10}} }
        }
    };
    using var content = JsonContent.Create(actions);
    var resp = await client.PostAsync(new Uri($"/api/sessions/{id}/inputs", UriKind.Relative), content).ConfigureAwait(true);
    resp.StatusCode.Should().Be(HttpStatusCode.Accepted);
    // B-001: the response body now also carries a per-action "results" array (spec FR-011a/FR-012),
    // so this no longer deserializes as a flat Dictionary<string, int> — read "accepted" directly.
    var body = await resp.Content.ReadFromJsonAsync<JsonElement>().ConfigureAwait(true);
    body.GetProperty("accepted").GetInt32().Should().Be(3);
  }

  // B-001: a malformed action against a confirmed-running session used to come back as a
  // misleading 409 "not_running". It must now be a 400 that names the real problem instead.
  [Fact]
  public async Task PostingAMalformedSwipeToARunningSessionReturnsBadRequestNotConflict() {
    Environment.SetEnvironmentVariable("GAMEBOT_AUTH_TOKEN", "test-token");
    using var app = new WebApplicationFactory<Program>();
    var client = app.CreateClient();
    client.DefaultRequestHeaders.Add("Authorization", "Bearer test-token");

    var createResp = await client.PostAsJsonAsync(new Uri("/api/sessions", UriKind.Relative), new { gameId = "test-game" }).ConfigureAwait(true);
    var created = await createResp.Content.ReadFromJsonAsync<Dictionary<string, object>>().ConfigureAwait(true);
    var id = created!["id"].ToString();

    var getResp = await client.GetAsync(new Uri($"/api/sessions/{id}", UriKind.Relative)).ConfigureAwait(true);
    getResp.StatusCode.Should().Be(HttpStatusCode.OK, "the session must be confirmed running before this assertion means anything");

    var actions = new { actions = new[] { new { type = "swipe", args = new Dictionary<string, object> { { "x1", 1 }, { "y1", 2 } } } } };
    using var content = JsonContent.Create(actions);
    var resp = await client.PostAsync(new Uri($"/api/sessions/{id}/inputs", UriKind.Relative), content).ConfigureAwait(true);

    resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    var body = await resp.Content.ReadFromJsonAsync<JsonElement>().ConfigureAwait(true);
    body.GetProperty("error").GetProperty("code").GetString().Should().Be("invalid_input_actions");
  }

  [Fact]
  public async Task PostingOneGoodAndOneMalformedActionReturnsAcceptedWithPerActionResults() {
    Environment.SetEnvironmentVariable("GAMEBOT_AUTH_TOKEN", "test-token");
    using var app = new WebApplicationFactory<Program>();
    var client = app.CreateClient();
    client.DefaultRequestHeaders.Add("Authorization", "Bearer test-token");

    var createResp = await client.PostAsJsonAsync(new Uri("/api/sessions", UriKind.Relative), new { gameId = "test-game" }).ConfigureAwait(true);
    var created = await createResp.Content.ReadFromJsonAsync<Dictionary<string, object>>().ConfigureAwait(true);
    var id = created!["id"].ToString();

    var actions = new {
      actions = new object[] {
        new { type = "tap", args = new Dictionary<string, object> { { "x", 10 }, { "y", 10 } } },
        new { type = "swipe", args = new Dictionary<string, object> { { "x1", 1 }, { "y1", 2 } } }
      }
    };
    using var content = JsonContent.Create(actions);
    var resp = await client.PostAsync(new Uri($"/api/sessions/{id}/inputs", UriKind.Relative), content).ConfigureAwait(true);

    resp.StatusCode.Should().Be(HttpStatusCode.Accepted);
    var body = await resp.Content.ReadFromJsonAsync<JsonElement>().ConfigureAwait(true);
    body.GetProperty("accepted").GetInt32().Should().Be(1);
    var results = body.GetProperty("results");
    results.GetArrayLength().Should().Be(2);
    results[0].GetProperty("dispatched").GetBoolean().Should().BeTrue();
    results[1].GetProperty("dispatched").GetBoolean().Should().BeFalse();
    results[1].GetProperty("failureReason").GetString().Should().NotBeNullOrWhiteSpace();
  }

  // Regression: a genuinely non-existent session must keep returning 409 not_running unchanged.
  [Fact]
  public async Task PostingInputsToANonExistentSessionStillReturnsConflictNotRunning() {
    Environment.SetEnvironmentVariable("GAMEBOT_AUTH_TOKEN", "test-token");
    using var app = new WebApplicationFactory<Program>();
    var client = app.CreateClient();
    client.DefaultRequestHeaders.Add("Authorization", "Bearer test-token");

    var actions = new { actions = new[] { new { type = "tap", args = new Dictionary<string, object> { { "x", 10 }, { "y", 10 } } } } };
    using var content = JsonContent.Create(actions);
    var resp = await client.PostAsync(new Uri("/api/sessions/no-such-session/inputs", UriKind.Relative), content).ConfigureAwait(true);

    resp.StatusCode.Should().Be(HttpStatusCode.Conflict);
    var body = await resp.Content.ReadFromJsonAsync<JsonElement>().ConfigureAwait(true);
    body.GetProperty("error").GetProperty("code").GetString().Should().Be("not_running");
  }
}
