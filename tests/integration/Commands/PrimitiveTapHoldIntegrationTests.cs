using System.Net;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace GameBot.IntegrationTests.Commands;

/// <summary>
/// Feature 111 (issue #235): the hold duration of a PrimitiveTap step survives save and read-back, and a value
/// outside 0 to 5000 gets 400. Before the feature, the service dropped the field.
/// </summary>
[Collection("ConfigIsolation")]
public sealed class PrimitiveTapHoldIntegrationTests : IDisposable {
  private const string RangeError = "primitiveTap.holdMs must be between 0 and 5000";

  private readonly string? _prevUseAdb;
  private readonly string? _prevDynamicPort;
  private readonly string? _prevAuthToken;
  private readonly string? _prevDataDir;

  public PrimitiveTapHoldIntegrationTests() {
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

  private static async Task<HttpResponseMessage> SendJsonAsync(HttpClient client, HttpMethod method, string uri, string body) {
    using var content = new StringContent(body, Encoding.UTF8, "application/json");
    using var request = new HttpRequestMessage(method, new Uri(uri, UriKind.Relative)) { Content = content };
    return await client.SendAsync(request).ConfigureAwait(false);
  }

  private static string TapStep(string holdMsJson, string extra = "") =>
    "{ \"type\": \"PrimitiveTap\", \"order\": 0, \"primitiveTap\": { \"detectionTarget\": { \"referenceImageId\": \"claim-button\", \"confidence\": 0.9 }"
    + holdMsJson + " }" + extra + " }";

  private static string CreateBody(string step) => "{ \"name\": \"Claim\", \"steps\": [ " + step + " ] }";

  private static async Task<string> CreateAsync(HttpClient client, string step) {
    using var response = await SendJsonAsync(client, HttpMethod.Post, "/api/commands", CreateBody(step)).ConfigureAwait(false);
    var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
    response.StatusCode.Should().Be(HttpStatusCode.Created, body);
    using var doc = JsonDocument.Parse(body);
    return doc.RootElement.GetProperty("id").GetString()!;
  }

  private static async Task<JsonElement> ReadPrimitiveTapAsync(HttpClient client, string id) {
    var response = await client.GetAsync(new Uri($"/api/commands/{id}", UriKind.Relative)).ConfigureAwait(false);
    var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
    response.StatusCode.Should().Be(HttpStatusCode.OK, body);
    using var doc = JsonDocument.Parse(body);
    return doc.RootElement.GetProperty("steps")[0].GetProperty("primitiveTap").Clone();
  }

  [Theory]
  [InlineData(0)]
  [InlineData(700)]
  [InlineData(5000)]
  public async Task CreateKeepsTheHoldDurationOnReadBack(int holdMs) {
    using var app = new WebApplicationFactory<Program>();
    using var client = CreateClient(app);

    var id = await CreateAsync(client, TapStep($", \"holdMs\": {holdMs}")).ConfigureAwait(false);

    var tap = await ReadPrimitiveTapAsync(client, id).ConfigureAwait(false);
    tap.GetProperty("holdMs").GetInt32().Should().Be(holdMs);
    tap.GetProperty("detectionTarget").GetProperty("referenceImageId").GetString().Should().Be("claim-button");
  }

  [Fact]
  public async Task CreateWithoutHoldDurationHasNoHoldDurationOnReadBack() {
    using var app = new WebApplicationFactory<Program>();
    using var client = CreateClient(app);

    var id = await CreateAsync(client, TapStep(string.Empty)).ConfigureAwait(false);

    var tap = await ReadPrimitiveTapAsync(client, id).ConfigureAwait(false);
    tap.TryGetProperty("holdMs", out _).Should().BeFalse();
  }

  [Fact]
  public async Task UpdateChangesTheHoldDuration() {
    using var app = new WebApplicationFactory<Program>();
    using var client = CreateClient(app);
    var id = await CreateAsync(client, TapStep(", \"holdMs\": 700")).ConfigureAwait(false);

    using var patch = await SendJsonAsync(client, HttpMethod.Patch, $"/api/commands/{id}", "{ \"steps\": [ " + TapStep(", \"holdMs\": 1000") + " ] }").ConfigureAwait(false);
    patch.StatusCode.Should().Be(HttpStatusCode.OK, await patch.Content.ReadAsStringAsync().ConfigureAwait(false));

    var tap = await ReadPrimitiveTapAsync(client, id).ConfigureAwait(false);
    tap.GetProperty("holdMs").GetInt32().Should().Be(1000);
  }

  [Theory]
  [InlineData(-1)]
  [InlineData(5001)]
  public async Task CreateRejectsAHoldDurationOutsideTheRange(int holdMs) {
    using var app = new WebApplicationFactory<Program>();
    using var client = CreateClient(app);

    using var response = await SendJsonAsync(client, HttpMethod.Post, "/api/commands", CreateBody(TapStep($", \"holdMs\": {holdMs}"))).ConfigureAwait(false);

    response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    (await response.Content.ReadAsStringAsync().ConfigureAwait(false)).Should().Contain(RangeError);
  }

  [Fact]
  public async Task UpdateRejectsAHoldDurationOutsideTheRange() {
    using var app = new WebApplicationFactory<Program>();
    using var client = CreateClient(app);
    var id = await CreateAsync(client, TapStep(", \"holdMs\": 700")).ConfigureAwait(false);

    using var patch = await SendJsonAsync(client, HttpMethod.Patch, $"/api/commands/{id}", "{ \"steps\": [ " + TapStep(", \"holdMs\": 5001") + " ] }").ConfigureAwait(false);

    patch.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    (await patch.Content.ReadAsStringAsync().ConfigureAwait(false)).Should().Contain(RangeError);
    var tap = await ReadPrimitiveTapAsync(client, id).ConfigureAwait(false);
    tap.GetProperty("holdMs").GetInt32().Should().Be(700);
  }

  [Fact]
  public async Task ExecuteStepRejectsAHoldDurationOutsideTheRange() {
    using var app = new WebApplicationFactory<Program>();
    using var client = CreateClient(app);

    using var response = await SendJsonAsync(client, HttpMethod.Post, "/api/steps/execute", "{ \"step\": " + TapStep(", \"holdMs\": 5001") + " }").ConfigureAwait(false);

    response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    (await response.Content.ReadAsStringAsync().ConfigureAwait(false)).Should().Contain(RangeError);
  }

  [Fact]
  public async Task CreateRejectsAFieldTemplateForTheHoldDuration() {
    using var app = new WebApplicationFactory<Program>();
    using var client = CreateClient(app);

    var step = TapStep(", \"holdMs\": 700", ", \"fieldTemplates\": { \"primitiveTap.holdMs\": \"{{hold}}\" }");
    var body = "{ \"name\": \"Claim\", \"parameters\": [ { \"name\": \"hold\", \"type\": \"number\" } ], \"steps\": [ " + step + " ] }";

    using var response = await SendJsonAsync(client, HttpMethod.Post, "/api/commands", body).ConfigureAwait(false);

    response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    (await response.Content.ReadAsStringAsync().ConfigureAwait(false)).Should().Contain("primitiveTap.holdMs");
  }
}
