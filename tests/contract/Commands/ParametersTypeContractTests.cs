using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

#pragma warning disable CA1861, CA1062, CA1707

namespace GameBot.ContractTests.Commands;

/// <summary>
/// Feature 136: a command request with a wrong type for "parameters" gives 400 that names "parameters".
/// Nothing is stored or changed. Valid requests keep their answers.
/// </summary>
public sealed class ParametersTypeContractTests {
  private const string ExpectedMessage = "parameters must be a list of parameter declarations";
  private const string ExpectedHint = "Send an array, even for one declaration.";

  private static WebApplicationFactory<Program> CreateFactory() {
    Environment.SetEnvironmentVariable("GAMEBOT_USE_ADB", "false");
    Environment.SetEnvironmentVariable("GAMEBOT_DYNAMIC_PORT", "true");
    Environment.SetEnvironmentVariable("GAMEBOT_AUTH_TOKEN", "test-token");
    return new WebApplicationFactory<Program>();
  }

  private static HttpClient AuthedClient(WebApplicationFactory<Program> app) {
    var client = app.CreateClient();
    client.DefaultRequestHeaders.Add("Authorization", "Bearer test-token");
    return client;
  }

  private static string UniqueName() => $"params-type-{Guid.NewGuid():N}";

  private static Dictionary<string, object?> TapStep() =>
    new() {
      ["type"] = "PrimitiveTap",
      ["order"] = 0,
      ["primitiveTap"] = new Dictionary<string, object?> {
        ["detectionTarget"] = new Dictionary<string, object?> { ["referenceImageId"] = "exchange-button", ["confidence"] = 0.9 }
      }
    };

  private static Dictionary<string, object?> Body(string name, object? parameters, bool includeParameters = true) {
    var body = new Dictionary<string, object?> { ["name"] = name, ["steps"] = new object[] { TapStep() } };
    if (includeParameters) body["parameters"] = parameters;
    return body;
  }

  private static async Task<(HttpStatusCode Status, string Text, JsonElement Json)> SendAsync(HttpClient client, HttpMethod method, string url, object payload) {
    using var req = new HttpRequestMessage(method, url) { Content = JsonContent.Create(payload) };
    return await ReadAsync(client, req).ConfigureAwait(false);
  }

  private static async Task<(HttpStatusCode Status, string Text, JsonElement Json)> SendRawAsync(HttpClient client, HttpMethod method, string url, string raw) {
    using var req = new HttpRequestMessage(method, url) { Content = new StringContent(raw, Encoding.UTF8, "application/json") };
    return await ReadAsync(client, req).ConfigureAwait(false);
  }

  private static async Task<(HttpStatusCode Status, string Text, JsonElement Json)> ReadAsync(HttpClient client, HttpRequestMessage req) {
    using var resp = await client.SendAsync(req).ConfigureAwait(false);
    var text = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
    JsonElement json = default;
    if (!string.IsNullOrWhiteSpace(text)) {
      try { json = JsonDocument.Parse(text).RootElement.Clone(); } catch (JsonException) { /* not JSON */ }
    }
    return (resp.StatusCode, text, json);
  }

  private static async Task<string> CreateValidAsync(HttpClient client, string name) {
    var r = await SendAsync(client, HttpMethod.Post, "/api/commands", Body(name, null, includeParameters: false)).ConfigureAwait(false);
    r.Status.Should().Be(HttpStatusCode.Created, r.Text);
    return r.Json.GetProperty("id").GetString()!;
  }

  private static async Task<int> CountByNameAsync(HttpClient client, string name) {
    var list = await client.GetFromJsonAsync<JsonElement>(new Uri("/api/commands", UriKind.Relative)).ConfigureAwait(false);
    return list.EnumerateArray().Count(c => c.GetProperty("name").GetString() == name);
  }

  private static void ShouldRejectParameters(JsonElement json) {
    var error = json.GetProperty("error");
    error.GetProperty("code").GetString().Should().Be("invalid_request");
    error.GetProperty("message").GetString().Should().Be(ExpectedMessage);
    error.GetProperty("hint").GetString().Should().Be(ExpectedHint);
  }

  private static readonly object ObjectDeclaration = new Dictionary<string, object?> { ["name"] = "novaOptionImage", ["type"] = "text", ["required"] = true };

  // ---- User Story 1: POST ----

  [Fact]
  public async Task Post_ParametersAsObject_Gives400WithMessageAndHint() {
    using var app = CreateFactory();
    var client = AuthedClient(app);

    var r = await SendAsync(client, HttpMethod.Post, "/api/commands", Body(UniqueName(), ObjectDeclaration)).ConfigureAwait(false);

    r.Status.Should().Be(HttpStatusCode.BadRequest, r.Text);
    ShouldRejectParameters(r.Json);
  }

  [Fact]
  public async Task Post_ParametersAsObject_StoresNothing() {
    using var app = CreateFactory();
    var client = AuthedClient(app);
    var name = UniqueName();

    var r = await SendAsync(client, HttpMethod.Post, "/api/commands", Body(name, ObjectDeclaration)).ConfigureAwait(false);

    r.Status.Should().Be(HttpStatusCode.BadRequest, r.Text);
    (await CountByNameAsync(client, name).ConfigureAwait(false)).Should().Be(0);
  }

  [Theory]
  [InlineData("\"text\"")]
  [InlineData("5")]
  [InlineData("true")]
  [InlineData("[1]")]
  public async Task Post_ParametersWithBadType_Gives400(string rawValue) {
    using var app = CreateFactory();
    var client = AuthedClient(app);
    var name = UniqueName();
    var raw = "{\"name\":\"" + name + "\",\"steps\":[{\"type\":\"PrimitiveTap\",\"order\":0,\"primitiveTap\":{\"detectionTarget\":{\"referenceImageId\":\"exchange-button\"}}}],\"parameters\":" + rawValue + "}";

    var r = await SendRawAsync(client, HttpMethod.Post, "/api/commands", raw).ConfigureAwait(false);

    r.Status.Should().Be(HttpStatusCode.BadRequest, r.Text);
    ShouldRejectParameters(r.Json);
    (await CountByNameAsync(client, name).ConfigureAwait(false)).Should().Be(0);
  }

  [Fact]
  public async Task Post_WrongTypeInsideDeclaration_Gives400NamingParameters() {
    using var app = CreateFactory();
    var client = AuthedClient(app);
    var name = UniqueName();
    var items = new object[] { new Dictionary<string, object?> { ["name"] = "p", ["type"] = "text", ["required"] = "yes" } };

    var r = await SendAsync(client, HttpMethod.Post, "/api/commands", Body(name, items)).ConfigureAwait(false);

    r.Status.Should().Be(HttpStatusCode.BadRequest, r.Text);
    r.Json.GetProperty("error").GetProperty("code").GetString().Should().Be("invalid_request");
    r.Json.GetProperty("error").GetProperty("message").GetString().Should().Contain("parameters");
    (await CountByNameAsync(client, name).ConfigureAwait(false)).Should().Be(0);
  }

  // ---- User Story 2: valid requests keep working ----

  [Fact]
  public async Task Post_ValidParameterList_Gives201() {
    using var app = CreateFactory();
    var client = AuthedClient(app);
    var items = new object[] { new Dictionary<string, object?> { ["name"] = "p", ["type"] = "text", ["required"] = true } };

    var r = await SendAsync(client, HttpMethod.Post, "/api/commands", Body(UniqueName(), items)).ConfigureAwait(false);

    r.Status.Should().Be(HttpStatusCode.Created, r.Text);
  }

  [Fact]
  public async Task Post_NoParameters_Gives201() {
    using var app = CreateFactory();
    var client = AuthedClient(app);

    var r = await SendAsync(client, HttpMethod.Post, "/api/commands", Body(UniqueName(), null, includeParameters: false)).ConfigureAwait(false);

    r.Status.Should().Be(HttpStatusCode.Created, r.Text);
  }

  [Fact]
  public async Task Post_NullParameters_KeepsRecordedAnswer() {
    using var app = CreateFactory();
    var client = AuthedClient(app);

    var r = await SendAsync(client, HttpMethod.Post, "/api/commands", Body(UniqueName(), null)).ConfigureAwait(false);

    r.Status.Should().Be(HttpStatusCode.Created, r.Text);
  }

  [Fact]
  public async Task Post_EmptyParameterList_KeepsRecordedAnswer() {
    using var app = CreateFactory();
    var client = AuthedClient(app);

    var r = await SendAsync(client, HttpMethod.Post, "/api/commands", Body(UniqueName(), Array.Empty<object>())).ConfigureAwait(false);

    r.Status.Should().Be(HttpStatusCode.Created, r.Text);
  }

  [Fact]
  public async Task Post_WrongTypeInOtherField_KeepsRecordedAnswerAndDoesNotNameParameters() {
    using var app = CreateFactory();
    var client = AuthedClient(app);
    var body = Body(UniqueName(), null, includeParameters: false);
    body["name"] = 5;

    var r = await SendAsync(client, HttpMethod.Post, "/api/commands", body).ConfigureAwait(false);

    r.Status.Should().Be(HttpStatusCode.InternalServerError, r.Text);
    r.Text.Should().NotContain("parameters must be a list");
  }

  // ---- User Story 3: PATCH ----

  [Fact]
  public async Task Patch_ParametersAsObject_Gives400AndLeavesCommandUnchanged() {
    using var app = CreateFactory();
    var client = AuthedClient(app);
    var name = UniqueName();
    var id = await CreateValidAsync(client, name).ConfigureAwait(false);

    var r = await SendAsync(client, HttpMethod.Patch, $"/api/commands/{id}",
      new Dictionary<string, object?> { ["name"] = "renamed-" + name, ["parameters"] = ObjectDeclaration }).ConfigureAwait(false);

    r.Status.Should().Be(HttpStatusCode.BadRequest, r.Text);
    ShouldRejectParameters(r.Json);
    var after = await client.GetFromJsonAsync<JsonElement>(new Uri($"/api/commands/{id}", UriKind.Relative)).ConfigureAwait(false);
    after.GetProperty("name").GetString().Should().Be(name);
  }

  [Theory]
  [InlineData("\"text\"")]
  [InlineData("5")]
  [InlineData("true")]
  [InlineData("[1]")]
  public async Task Patch_ParametersWithBadType_Gives400(string rawValue) {
    using var app = CreateFactory();
    var client = AuthedClient(app);
    var id = await CreateValidAsync(client, UniqueName()).ConfigureAwait(false);

    var r = await SendRawAsync(client, HttpMethod.Patch, $"/api/commands/{id}", "{\"parameters\":" + rawValue + "}").ConfigureAwait(false);

    r.Status.Should().Be(HttpStatusCode.BadRequest, r.Text);
    ShouldRejectParameters(r.Json);
  }

  [Fact]
  public async Task Patch_ParametersAsObject_OnMissingId_Gives400Not404() {
    using var app = CreateFactory();
    var client = AuthedClient(app);

    var r = await SendAsync(client, HttpMethod.Patch, "/api/commands/no-such-command",
      new Dictionary<string, object?> { ["parameters"] = ObjectDeclaration }).ConfigureAwait(false);

    r.Status.Should().Be(HttpStatusCode.BadRequest, r.Text);
    ShouldRejectParameters(r.Json);
  }

  [Fact]
  public async Task Patch_NullParameters_KeepsRecordedAnswer() {
    using var app = CreateFactory();
    var client = AuthedClient(app);
    var id = await CreateValidAsync(client, UniqueName()).ConfigureAwait(false);

    var r = await SendRawAsync(client, HttpMethod.Patch, $"/api/commands/{id}", "{\"parameters\":null}").ConfigureAwait(false);

    r.Status.Should().Be(HttpStatusCode.OK, r.Text);
  }

  [Fact]
  public async Task Patch_EmptyParameterList_KeepsRecordedAnswer() {
    using var app = CreateFactory();
    var client = AuthedClient(app);
    var id = await CreateValidAsync(client, UniqueName()).ConfigureAwait(false);

    var r = await SendRawAsync(client, HttpMethod.Patch, $"/api/commands/{id}", "{\"parameters\":[]}").ConfigureAwait(false);

    r.Status.Should().Be(HttpStatusCode.OK, r.Text);
  }

  [Fact]
  public async Task Patch_WrongTypeInOtherField_KeepsRecordedAnswerAndDoesNotNameParameters() {
    using var app = CreateFactory();
    var client = AuthedClient(app);
    var id = await CreateValidAsync(client, UniqueName()).ConfigureAwait(false);

    var r = await SendRawAsync(client, HttpMethod.Patch, $"/api/commands/{id}", "{\"name\":5}").ConfigureAwait(false);

    r.Status.Should().Be(HttpStatusCode.InternalServerError, r.Text);
    r.Text.Should().NotContain("parameters must be a list");
  }

  [Fact]
  public async Task Patch_ValidUpdate_Gives200() {
    using var app = CreateFactory();
    var client = AuthedClient(app);
    var id = await CreateValidAsync(client, UniqueName()).ConfigureAwait(false);

    var r = await SendAsync(client, HttpMethod.Patch, $"/api/commands/{id}",
      new { name = "patched-" + Guid.NewGuid().ToString("N") }).ConfigureAwait(false);

    r.Status.Should().Be(HttpStatusCode.OK, r.Text);
  }
}
