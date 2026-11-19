using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace GameBot.IntegrationTests.Commands;

/// <summary>Feature 192: the command routes and the steps route accept, check, and run a TextInput step.</summary>
[Collection("ConfigIsolation")]
public sealed class TextInputStepEndpointTests : IDisposable {
  private readonly string? _prevUseAdb;
  private readonly string? _prevDynamicPort;
  private readonly string? _prevAuthToken;
  private readonly string? _prevDataDir;

  public TextInputStepEndpointTests() {
    _prevUseAdb = Environment.GetEnvironmentVariable("GAMEBOT_USE_ADB");
    _prevDynamicPort = Environment.GetEnvironmentVariable("GAMEBOT_DYNAMIC_PORT");
    _prevAuthToken = Environment.GetEnvironmentVariable("GAMEBOT_AUTH_TOKEN");
    _prevDataDir = Environment.GetEnvironmentVariable("GAMEBOT_DATA_DIR");

    Environment.SetEnvironmentVariable("GAMEBOT_USE_ADB", "false");
    Environment.SetEnvironmentVariable("GAMEBOT_DYNAMIC_PORT", "true");
    Environment.SetEnvironmentVariable("GAMEBOT_AUTH_TOKEN", "test-token");
    TestEnvironment.PrepareCleanDataDir();
  }

  public void Dispose() {
    Environment.SetEnvironmentVariable("GAMEBOT_USE_ADB", _prevUseAdb);
    Environment.SetEnvironmentVariable("GAMEBOT_DYNAMIC_PORT", _prevDynamicPort);
    Environment.SetEnvironmentVariable("GAMEBOT_AUTH_TOKEN", _prevAuthToken);
    Environment.SetEnvironmentVariable("GAMEBOT_DATA_DIR", _prevDataDir);
    GC.SuppressFinalize(this);
  }

  private static HttpClient CreateClient(WebApplicationFactory<Program> app) {
    var client = app.CreateClient();
    client.DefaultRequestHeaders.Add("Authorization", "Bearer test-token");
    return client;
  }

  [Fact]
  public async Task ACommandWithATextInputStepSavesAndLoads() {
    using var app = new WebApplicationFactory<Program>();
    using var client = CreateClient(app);

    var create = await client.PostAsJsonAsync(new Uri("/api/commands", UriKind.Relative), new {
      name = "Type amount",
      steps = new object[] { new { type = "TextInput", order = 0, textInput = new { text = "250" } } }
    }).ConfigureAwait(false);
    var createBody = await create.Content.ReadAsStringAsync().ConfigureAwait(false);
    create.StatusCode.Should().Be(HttpStatusCode.Created, createBody);
    var id = JsonDocument.Parse(createBody).RootElement.GetProperty("id").GetString();

    var get = await client.GetStringAsync(new Uri($"/api/commands/{id}", UriKind.Relative)).ConfigureAwait(false);
    using var doc = JsonDocument.Parse(get);
    var step = doc.RootElement.GetProperty("steps")[0];
    step.GetProperty("type").GetString().Should().Be("TextInput");
    step.GetProperty("textInput").GetProperty("text").GetString().Should().Be("250");
  }

  public static TheoryData<string, object> BadSteps() => new() {
    { "textInput.text is required for TextInput steps", new { type = "TextInput", order = 0 } },
    { "textInput.text is required for TextInput steps", new { type = "TextInput", order = 0, textInput = new { text = "" } } },
    { "textInput.text must have 500 characters or fewer", new { type = "TextInput", order = 0, textInput = new { text = new string('1', 501) } } },
    { "textInput.text can have only the printable ASCII characters from space to ~", new { type = "TextInput", order = 0, textInput = new { text = "é" } } },
  };

  [Theory]
  [MemberData(nameof(BadSteps))]
  public async Task ABadTextInputStepGives400WithTheMessage(string message, object step) {
    using var app = new WebApplicationFactory<Program>();
    using var client = CreateClient(app);

    var response = await client.PostAsJsonAsync(new Uri("/api/commands", UriKind.Relative), new {
      name = "Bad", steps = new[] { step }
    }).ConfigureAwait(false);
    var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

    response.StatusCode.Should().Be(HttpStatusCode.BadRequest, body);
    using var doc = JsonDocument.Parse(body);
    doc.RootElement.GetProperty("error").GetProperty("code").GetString().Should().Be("invalid_request");
    doc.RootElement.GetProperty("error").GetProperty("message").GetString().Should().Be(message);
  }

  [Fact]
  public async Task TheStepsRouteRunsATextInputStepAndReportsTheTypedLength() {
    using var app = new WebApplicationFactory<Program>();
    using var client = CreateClient(app);
    var gameResp = await client.PostAsJsonAsync(new Uri("/api/games", UriKind.Relative), new { name = "TextGame", description = "desc" }).ConfigureAwait(false);
    gameResp.EnsureSuccessStatusCode();
    var game = await gameResp.Content.ReadFromJsonAsync<Dictionary<string, object>>().ConfigureAwait(false);
    var sessionResp = await client.PostAsJsonAsync(new Uri("/api/sessions", UriKind.Relative), new { gameId = game!["id"]!.ToString() }).ConfigureAwait(false);
    sessionResp.EnsureSuccessStatusCode();
    var session = await sessionResp.Content.ReadFromJsonAsync<Dictionary<string, object>>().ConfigureAwait(false);

    var response = await client.PostAsJsonAsync(new Uri("/api/steps/execute", UriKind.Relative), new {
      sessionId = session!["id"]!.ToString(),
      step = new { type = "TextInput", order = 0, textInput = new { text = "250" } }
    }).ConfigureAwait(false);
    var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

    response.StatusCode.Should().Be(HttpStatusCode.Accepted, body);
    using var doc = JsonDocument.Parse(body);
    var outcome = doc.RootElement.GetProperty("stepOutcomes")[0];
    outcome.GetProperty("status").GetString().Should().Be("executed");
    outcome.GetProperty("stepType").GetString().Should().Be("text-input");
    outcome.GetProperty("typedLength").GetInt32().Should().Be(3);
  }

  [Fact]
  public async Task TheStepsRouteRejectsAnEmptyLiteralText() {
    using var app = new WebApplicationFactory<Program>();
    using var client = CreateClient(app);

    var response = await client.PostAsJsonAsync(new Uri("/api/steps/execute", UriKind.Relative), new {
      step = new { type = "TextInput", order = 0, textInput = new { text = "" } }
    }).ConfigureAwait(false);

    response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    (await response.Content.ReadAsStringAsync().ConfigureAwait(false)).Should().Contain("textInput.text is required");
  }
}
