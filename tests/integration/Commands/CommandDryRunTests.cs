using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace GameBot.IntegrationTests.Commands;

/// <summary>Feature 192 (US4): dryRun on the command create and update routes runs all checks and stores nothing.</summary>
[Collection("ConfigIsolation")]
public sealed class CommandDryRunTests : IDisposable {
  private readonly string? _prevUseAdb;
  private readonly string? _prevDynamicPort;
  private readonly string? _prevAuthToken;
  private readonly string? _prevDataDir;

  public CommandDryRunTests() {
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

  private static object Body(bool? dryRun, object step, bool declare = true) => new {
    name = "Type amount",
    dryRun,
    parameters = declare ? new object[] { new { name = "fighterBatch", type = "number", @default = "250" } } : Array.Empty<object>(),
    steps = new[] { step }
  };

  private static object OverlayStep(string name) => new {
    type = "TextInput", order = 0, textInput = new { text = "" },
    fieldTemplates = new Dictionary<string, string> { ["textInput.text"] = "{{" + name + "}}" }
  };

  private static object InlineStep(string text) => new { type = "TextInput", order = 0, textInput = new { text } };

  private static async Task<int> CommandCountAsync(HttpClient client) {
    var list = await client.GetStringAsync(new Uri("/api/commands", UriKind.Relative)).ConfigureAwait(false);
    return JsonDocument.Parse(list).RootElement.GetArrayLength();
  }

  [Fact]
  public async Task AValidCreateDryRunGives200AndStoresNothing() {
    using var app = new WebApplicationFactory<Program>();
    using var client = CreateClient(app);
    var before = await CommandCountAsync(client).ConfigureAwait(false);

    var response = await client.PostAsJsonAsync(new Uri("/api/commands", UriKind.Relative), Body(true, OverlayStep("fighterBatch"))).ConfigureAwait(false);
    var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

    response.StatusCode.Should().Be(HttpStatusCode.OK, body);
    using var doc = JsonDocument.Parse(body);
    doc.RootElement.GetProperty("valid").GetBoolean().Should().BeTrue();
    doc.RootElement.GetProperty("dryRun").GetBoolean().Should().BeTrue();
    doc.RootElement.GetProperty("errors").GetArrayLength().Should().Be(0);
    (await CommandCountAsync(client).ConfigureAwait(false)).Should().Be(before);
  }

  [Theory]
  [InlineData(true)]
  [InlineData(false)]
  public async Task AnUnknownPlaceholderInTheTextGives400WithTheFieldPath(bool overlay) {
    using var app = new WebApplicationFactory<Program>();
    using var client = CreateClient(app);
    var step = overlay ? OverlayStep("fighterBach") : InlineStep("{{fighterBach}}");

    var response = await client.PostAsJsonAsync(new Uri("/api/commands", UriKind.Relative), Body(true, step)).ConfigureAwait(false);
    var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

    response.StatusCode.Should().Be(HttpStatusCode.BadRequest, body);
    body.Should().Contain("unresolvable_parameter_reference").And.Contain("textInput.text").And.Contain("fighterBach");
  }

  [Fact]
  public async Task AStepErrorWithDryRunGivesTheSame400() {
    using var app = new WebApplicationFactory<Program>();
    using var client = CreateClient(app);

    var response = await client.PostAsJsonAsync(new Uri("/api/commands", UriKind.Relative), Body(true, InlineStep(""))).ConfigureAwait(false);
    var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

    response.StatusCode.Should().Be(HttpStatusCode.BadRequest, body);
    body.Should().Contain("textInput.text is required for TextInput steps");
  }

  [Fact]
  public async Task DryRunFalseStoresTheCommandAsBefore() {
    using var app = new WebApplicationFactory<Program>();
    using var client = CreateClient(app);

    var response = await client.PostAsJsonAsync(new Uri("/api/commands", UriKind.Relative), Body(false, InlineStep("250"))).ConfigureAwait(false);

    response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync().ConfigureAwait(false));
  }

  [Fact]
  public async Task AnUpdateDryRunGives200AndTheStoredCommandDoesNotChange() {
    using var app = new WebApplicationFactory<Program>();
    using var client = CreateClient(app);
    var create = await client.PostAsJsonAsync(new Uri("/api/commands", UriKind.Relative), Body(null, InlineStep("250"))).ConfigureAwait(false);
    var createBody = await create.Content.ReadAsStringAsync().ConfigureAwait(false);
    create.StatusCode.Should().Be(HttpStatusCode.Created, createBody);
    var id = JsonDocument.Parse(createBody).RootElement.GetProperty("id").GetString();

    var patch = await client.PatchAsJsonAsync(new Uri($"/api/commands/{id}", UriKind.Relative), new {
      name = "Changed", dryRun = true, steps = new[] { InlineStep("100") }
    }).ConfigureAwait(false);
    var patchBody = await patch.Content.ReadAsStringAsync().ConfigureAwait(false);

    patch.StatusCode.Should().Be(HttpStatusCode.OK, patchBody);
    JsonDocument.Parse(patchBody).RootElement.GetProperty("dryRun").GetBoolean().Should().BeTrue();
    var stored = JsonDocument.Parse(await client.GetStringAsync(new Uri($"/api/commands/{id}", UriKind.Relative)).ConfigureAwait(false)).RootElement;
    stored.GetProperty("name").GetString().Should().Be("Type amount");
    stored.GetProperty("steps")[0].GetProperty("textInput").GetProperty("text").GetString().Should().Be("250");
  }
}
