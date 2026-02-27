using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace GameBot.IntegrationTests.Commands;

[Collection("ConfigIsolation")]
public sealed class PrimitiveTapExecutionIntegrationTests : IDisposable {
  private readonly string? _prevUseAdb;
  private readonly string? _prevDynamicPort;
  private readonly string? _prevAuthToken;
  private readonly string? _prevDataDir;

  private const string OneByOnePngBase64 = "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO2n5u4AAAAASUVORK5CYII=";

  public PrimitiveTapExecutionIntegrationTests() {
    _prevUseAdb = Environment.GetEnvironmentVariable("GAMEBOT_USE_ADB");
    _prevDynamicPort = Environment.GetEnvironmentVariable("GAMEBOT_DYNAMIC_PORT");
    _prevAuthToken = Environment.GetEnvironmentVariable("GAMEBOT_AUTH_TOKEN");
    _prevDataDir = Environment.GetEnvironmentVariable("GAMEBOT_DATA_DIR");

    Environment.SetEnvironmentVariable("GAMEBOT_USE_ADB", "false");
    Environment.SetEnvironmentVariable("GAMEBOT_DYNAMIC_PORT", "true");
    Environment.SetEnvironmentVariable("GAMEBOT_AUTH_TOKEN", "test-token");
    Environment.SetEnvironmentVariable("GAMEBOT_TEST_SCREEN_IMAGE_B64", OneByOnePngBase64);
    TestEnvironment.PrepareCleanDataDir();
  }

  public void Dispose() {
    Environment.SetEnvironmentVariable("GAMEBOT_USE_ADB", _prevUseAdb);
    Environment.SetEnvironmentVariable("GAMEBOT_DYNAMIC_PORT", _prevDynamicPort);
    Environment.SetEnvironmentVariable("GAMEBOT_AUTH_TOKEN", _prevAuthToken);
    Environment.SetEnvironmentVariable("GAMEBOT_DATA_DIR", _prevDataDir);
    Environment.SetEnvironmentVariable("GAMEBOT_TEST_SCREEN_IMAGE_B64", null);
    GC.SuppressFinalize(this);
  }

  [Fact]
  public async Task ForceExecutePrimitiveTapReturnsExecutedStepOutcome() {
    using var app = new WebApplicationFactory<Program>();
    var client = app.CreateClient();
    client.DefaultRequestHeaders.Add("Authorization", "Bearer test-token");

    var uploadResp = await client.PostAsJsonAsync(new Uri("/api/images", UriKind.Relative), new { id = "home_button", data = OneByOnePngBase64 });
    uploadResp.StatusCode.Should().Be(HttpStatusCode.Created);

    var gameResp = await client.PostAsJsonAsync(new Uri("/api/games", UriKind.Relative), new { name = "PrimitiveTapGame", description = "desc" });
    gameResp.EnsureSuccessStatusCode();
    var game = await gameResp.Content.ReadFromJsonAsync<Dictionary<string, object>>();
    var gameId = game!["id"]!.ToString();

    var commandReq = new {
      name = "PrimitiveTapCmd",
      triggerId = (string?)null,
      steps = new[] {
        new {
          type = "PrimitiveTap",
          order = 0,
          primitiveTap = new {
            detectionTarget = new {
              referenceImageId = "home_button",
              confidence = 0.99,
              offsetX = 0,
              offsetY = 0,
              selectionStrategy = "HighestConfidence"
            }
          }
        }
      }
    };

    var commandResp = await client.PostAsJsonAsync(new Uri("/api/commands", UriKind.Relative), commandReq);
    commandResp.EnsureSuccessStatusCode();
    var command = await commandResp.Content.ReadFromJsonAsync<Dictionary<string, object>>();
    var commandId = command!["id"]!.ToString();

    var sessionResp = await client.PostAsJsonAsync(new Uri("/api/sessions", UriKind.Relative), new { gameId });
    sessionResp.EnsureSuccessStatusCode();
    var session = await sessionResp.Content.ReadFromJsonAsync<Dictionary<string, object>>();
    var sessionId = session!["id"]!.ToString();

    var execResp = await client.PostAsync(new Uri($"/api/commands/{commandId}/force-execute?sessionId={sessionId}", UriKind.Relative), null);
    execResp.StatusCode.Should().Be(HttpStatusCode.Accepted);

    using var doc = await System.Text.Json.JsonDocument.ParseAsync(await execResp.Content.ReadAsStreamAsync());
    doc.RootElement.GetProperty("accepted").GetInt32().Should().Be(1);
    var outcomes = doc.RootElement.GetProperty("stepOutcomes");
    outcomes.GetArrayLength().Should().Be(1);
    outcomes[0].GetProperty("stepOrder").GetInt32().Should().Be(0);
    outcomes[0].GetProperty("status").GetString().Should().Be("executed");
    outcomes[0].GetProperty("resolvedPoint").GetProperty("x").GetInt32().Should().Be(0);
    outcomes[0].GetProperty("resolvedPoint").GetProperty("y").GetInt32().Should().Be(0);
  }

  [Fact]
  public async Task ForceExecutePrimitiveTapWithLargeOffsetsReturnsPrimitiveStepOutcome() {
    using var app = new WebApplicationFactory<Program>();
    var client = app.CreateClient();
    client.DefaultRequestHeaders.Add("Authorization", "Bearer test-token");

    var uploadResp = await client.PostAsJsonAsync(new Uri("/api/images", UriKind.Relative), new { id = "home_button", data = OneByOnePngBase64 });
    uploadResp.StatusCode.Should().Be(HttpStatusCode.Created);

    var gameResp = await client.PostAsJsonAsync(new Uri("/api/games", UriKind.Relative), new { name = "PrimitiveTapBoundsGame", description = "desc" });
    gameResp.EnsureSuccessStatusCode();
    var game = await gameResp.Content.ReadFromJsonAsync<Dictionary<string, object>>();
    var gameId = game!["id"]!.ToString();

    var commandReq = new {
      name = "PrimitiveTapBoundsCmd",
      triggerId = (string?)null,
      steps = new[] {
        new {
          type = "PrimitiveTap",
          order = 0,
          primitiveTap = new {
            detectionTarget = new {
              referenceImageId = "home_button",
              confidence = 0.99,
              offsetX = 99999,
              offsetY = 99999
            }
          }
        }
      }
    };

    var commandResp = await client.PostAsJsonAsync(new Uri("/api/commands", UriKind.Relative), commandReq);
    commandResp.EnsureSuccessStatusCode();
    var command = await commandResp.Content.ReadFromJsonAsync<Dictionary<string, object>>();
    var commandId = command!["id"]!.ToString();

    var sessionResp = await client.PostAsJsonAsync(new Uri("/api/sessions", UriKind.Relative), new { gameId });
    sessionResp.EnsureSuccessStatusCode();
    var session = await sessionResp.Content.ReadFromJsonAsync<Dictionary<string, object>>();
    var sessionId = session!["id"]!.ToString();

    var execResp = await client.PostAsync(new Uri($"/api/commands/{commandId}/force-execute?sessionId={sessionId}", UriKind.Relative), null);
    execResp.StatusCode.Should().Be(HttpStatusCode.Accepted);

    using var doc = await System.Text.Json.JsonDocument.ParseAsync(await execResp.Content.ReadAsStreamAsync());
    var outcomes = doc.RootElement.GetProperty("stepOutcomes");
    outcomes.GetArrayLength().Should().Be(1);
    var status = outcomes[0].GetProperty("status").GetString();
    status.Should().BeOneOf("executed", "skipped_invalid_target");
  }

  [Fact]
  public async Task ForceExecutePrimitiveTapMissingTemplateSkipsWithoutTap() {
    using var app = new WebApplicationFactory<Program>();
    var client = app.CreateClient();
    client.DefaultRequestHeaders.Add("Authorization", "Bearer test-token");

    var gameResp = await client.PostAsJsonAsync(new Uri("/api/games", UriKind.Relative), new { name = "PrimitiveTapMissingTemplateGame", description = "desc" });
    gameResp.EnsureSuccessStatusCode();
    var game = await gameResp.Content.ReadFromJsonAsync<Dictionary<string, object>>();
    var gameId = game!["id"]!.ToString();

    var commandReq = new {
      name = "PrimitiveTapMissingTemplateCmd",
      triggerId = (string?)null,
      steps = new[] {
        new {
          type = "PrimitiveTap",
          order = 0,
          primitiveTap = new {
            detectionTarget = new {
              referenceImageId = "does_not_exist",
              confidence = 0.99,
              offsetX = 0,
              offsetY = 0
            }
          }
        }
      }
    };

    var commandResp = await client.PostAsJsonAsync(new Uri("/api/commands", UriKind.Relative), commandReq);
    commandResp.EnsureSuccessStatusCode();
    var command = await commandResp.Content.ReadFromJsonAsync<Dictionary<string, object>>();
    var commandId = command!["id"]!.ToString();

    var sessionResp = await client.PostAsJsonAsync(new Uri("/api/sessions", UriKind.Relative), new { gameId });
    sessionResp.EnsureSuccessStatusCode();
    var session = await sessionResp.Content.ReadFromJsonAsync<Dictionary<string, object>>();
    var sessionId = session!["id"]!.ToString();

    var execResp = await client.PostAsync(new Uri($"/api/commands/{commandId}/force-execute?sessionId={sessionId}", UriKind.Relative), null);
    execResp.StatusCode.Should().Be(HttpStatusCode.Accepted);

    using var doc = await System.Text.Json.JsonDocument.ParseAsync(await execResp.Content.ReadAsStreamAsync());
    doc.RootElement.GetProperty("accepted").GetInt32().Should().Be(0);
    var outcomes = doc.RootElement.GetProperty("stepOutcomes");
    outcomes.GetArrayLength().Should().Be(1);
    outcomes[0].GetProperty("status").GetString().Should().Be("skipped_invalid_config");
    outcomes[0].GetProperty("reason").GetString().Should().Be("template_not_found");
  }
}
