using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace GameBot.IntegrationTests.Commands;

/// <summary>
/// Feature 169: saving a command whose numeric field uses a friendly text parameter gives the error
/// <c>text_parameter_in_numeric_field</c>. A plain text parameter keeps its old behavior (feature 168, FR-018).
/// </summary>
[Collection("ConfigIsolation")]
public sealed class TextParameterInNumericFieldIntegrationTests : IDisposable {
  private readonly string? _prevUseAdb;
  private readonly string? _prevAuthToken;
  private readonly string? _prevDataDir;

  public TextParameterInNumericFieldIntegrationTests() {
    _prevUseAdb = Environment.GetEnvironmentVariable("GAMEBOT_USE_ADB");
    _prevAuthToken = Environment.GetEnvironmentVariable("GAMEBOT_AUTH_TOKEN");
    _prevDataDir = Environment.GetEnvironmentVariable("GAMEBOT_DATA_DIR");
    Environment.SetEnvironmentVariable("GAMEBOT_USE_ADB", "false");
    Environment.SetEnvironmentVariable("GAMEBOT_AUTH_TOKEN", "test-token");
    TestEnvironment.PrepareCleanDataDir();
  }

  public void Dispose() {
    Environment.SetEnvironmentVariable("GAMEBOT_USE_ADB", _prevUseAdb);
    Environment.SetEnvironmentVariable("GAMEBOT_AUTH_TOKEN", _prevAuthToken);
    Environment.SetEnvironmentVariable("GAMEBOT_DATA_DIR", _prevDataDir);
    GC.SuppressFinalize(this);
  }

  private static readonly string[] AllowedImages = { "home_button" };

  private static object Request(object parameter) => new {
    name = "SwipeWithParameter",
    parameters = new[] { parameter },
    steps = new[] {
      new {
        type = "Swipe",
        order = 0,
        swipe = new { startX = 1, startY = 2, endX = 3, endY = 4 },
        fieldTemplates = new Dictionary<string, string> { ["swipe.durationMs"] = "{{value}}" }
      }
    }
  };

  [Fact]
  public async Task ImageParameterInSwipeDurationIsRefused() {
    using var app = new WebApplicationFactory<Program>();
    var client = app.CreateClient();
    client.DefaultRequestHeaders.Add("Authorization", "Bearer test-token");

    var response = await client.PostAsJsonAsync(
        new Uri("/api/commands", UriKind.Relative),
        Request(new { name = "value", kind = "image", allowedImages = AllowedImages }));

    var body = await response.Content.ReadAsStringAsync();
    response.StatusCode.Should().Be(HttpStatusCode.BadRequest, body);
    body.Should().Contain("text_parameter_in_numeric_field");
    body.Should().Contain("swipe.durationMs");
  }

  [Fact]
  public async Task PlainTextParameterInSwipeDurationIsAccepted() {
    using var app = new WebApplicationFactory<Program>();
    var client = app.CreateClient();
    client.DefaultRequestHeaders.Add("Authorization", "Bearer test-token");

    var response = await client.PostAsJsonAsync(
        new Uri("/api/commands", UriKind.Relative),
        Request(new { name = "value", type = "text" }));

    response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
  }
}
