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
/// Feature 135: a command body with a field that the schema does not list gives 400 that names every
/// unknown path. Nothing is stored or changed. Valid bodies keep their answers.
/// </summary>
public sealed class UnknownStepFieldContractTests {
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

  private static string UniqueName() => $"unknown-field-{Guid.NewGuid():N}";

  private static Dictionary<string, object?> TapStep(Dictionary<string, object?>? tapExtra = null, Dictionary<string, object?>? stepExtra = null, Dictionary<string, object?>? targetExtra = null) {
    var target = new Dictionary<string, object?> { ["referenceImageId"] = "exchange-button", ["confidence"] = 0.9 };
    if (targetExtra is not null) foreach (var kv in targetExtra) target[kv.Key] = kv.Value;
    var tap = new Dictionary<string, object?> { ["detectionTarget"] = target };
    if (tapExtra is not null) foreach (var kv in tapExtra) tap[kv.Key] = kv.Value;
    var step = new Dictionary<string, object?> { ["type"] = "PrimitiveTap", ["order"] = 0, ["primitiveTap"] = tap };
    if (stepExtra is not null) foreach (var kv in stepExtra) step[kv.Key] = kv.Value;
    return step;
  }

  private static Dictionary<string, object?> Body(string name, params object[] steps) =>
    new() { ["name"] = name, ["steps"] = steps };

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
    var r = await SendAsync(client, HttpMethod.Post, "/api/commands", Body(name, TapStep())).ConfigureAwait(false);
    r.Status.Should().Be(HttpStatusCode.Created, r.Text);
    return r.Json.GetProperty("id").GetString()!;
  }

  private static async Task<int> CountByNameAsync(HttpClient client, string name) {
    var list = await client.GetFromJsonAsync<JsonElement>(new Uri("/api/commands", UriKind.Relative)).ConfigureAwait(false);
    return list.EnumerateArray().Count(c => c.GetProperty("name").GetString() == name);
  }

  private static void ShouldRejectWith(JsonElement json, params string[] paths) {
    var error = json.GetProperty("error");
    error.GetProperty("code").GetString().Should().Be("invalid_request");
    var message = error.GetProperty("message").GetString();
    foreach (var p in paths) message.Should().Contain(p);
    error.GetProperty("hint").GetString().Should().NotBeNullOrWhiteSpace();
  }

  // ---- User Story 1: POST ----

  [Fact]
  public async Task Post_UnknownPrimitiveTapField_Gives400WithPathAndHint() {
    using var app = CreateFactory();
    var client = AuthedClient(app);
    var name = UniqueName();

    var r = await SendAsync(client, HttpMethod.Post, "/api/commands",
      Body(name, TapStep(new() { ["bogusField"] = 5 }))).ConfigureAwait(false);

    r.Status.Should().Be(HttpStatusCode.BadRequest, r.Text);
    ShouldRejectWith(r.Json, "steps[0].primitiveTap.bogusField");
  }

  [Fact]
  public async Task Post_UnknownField_StoresNothing() {
    using var app = CreateFactory();
    var client = AuthedClient(app);
    var name = UniqueName();

    var r = await SendAsync(client, HttpMethod.Post, "/api/commands",
      Body(name, TapStep(new() { ["bogusField"] = 5 }))).ConfigureAwait(false);

    r.Status.Should().Be(HttpStatusCode.BadRequest, r.Text);
    (await CountByNameAsync(client, name).ConfigureAwait(false)).Should().Be(0);
  }

  [Fact]
  public async Task Post_UnknownFieldsInTwoPlaces_NamesBothPaths() {
    using var app = CreateFactory();
    var client = AuthedClient(app);

    var r = await SendAsync(client, HttpMethod.Post, "/api/commands",
      Body(UniqueName(),
        TapStep(new() { ["bogusOne"] = 1 }),
        TapStep(null, new() { ["bogusTwo"] = 2 }))).ConfigureAwait(false);

    r.Status.Should().Be(HttpStatusCode.BadRequest, r.Text);
    ShouldRejectWith(r.Json, "steps[0].primitiveTap.bogusOne", "steps[1].bogusTwo");
  }

  [Fact]
  public async Task Post_UnknownStepTopLevelField_NamesStepPath() {
    using var app = CreateFactory();
    var client = AuthedClient(app);

    var r = await SendAsync(client, HttpMethod.Post, "/api/commands",
      Body(UniqueName(), TapStep(null, new() { ["bogus"] = true }))).ConfigureAwait(false);

    r.Status.Should().Be(HttpStatusCode.BadRequest, r.Text);
    ShouldRejectWith(r.Json, "steps[0].bogus");
  }

  [Fact]
  public async Task Post_UnknownNestedField_NamesFullPath() {
    using var app = CreateFactory();
    var client = AuthedClient(app);

    var r = await SendAsync(client, HttpMethod.Post, "/api/commands",
      Body(UniqueName(), TapStep(null, null, new() { ["bogus"] = "x" }))).ConfigureAwait(false);

    r.Status.Should().Be(HttpStatusCode.BadRequest, r.Text);
    ShouldRejectWith(r.Json, "steps[0].primitiveTap.detectionTarget.bogus");
  }

  [Fact]
  public async Task Post_UnknownRootField_Gives400WithFieldName() {
    using var app = CreateFactory();
    var client = AuthedClient(app);
    var body = Body(UniqueName(), TapStep());
    body["bogusRoot"] = 1;

    var r = await SendAsync(client, HttpMethod.Post, "/api/commands", body).ConfigureAwait(false);

    r.Status.Should().Be(HttpStatusCode.BadRequest, r.Text);
    ShouldRejectWith(r.Json, "bogusRoot");
  }

  [Fact]
  public async Task Post_UnknownFieldWithNullValue_Gives400() {
    using var app = CreateFactory();
    var client = AuthedClient(app);

    var r = await SendAsync(client, HttpMethod.Post, "/api/commands",
      Body(UniqueName(), TapStep(new() { ["bogusField"] = null }))).ConfigureAwait(false);

    r.Status.Should().Be(HttpStatusCode.BadRequest, r.Text);
    ShouldRejectWith(r.Json, "steps[0].primitiveTap.bogusField");
  }

  // ---- User Story 2: valid requests keep working ----

  [Fact]
  public async Task Post_ValidRequest_Gives201() {
    using var app = CreateFactory();
    var client = AuthedClient(app);

    var r = await SendAsync(client, HttpMethod.Post, "/api/commands", Body(UniqueName(), TapStep())).ConfigureAwait(false);

    r.Status.Should().Be(HttpStatusCode.Created, r.Text);
  }

  [Fact]
  public async Task Post_HoldMs_IsStored() {
    using var app = CreateFactory();
    var client = AuthedClient(app);

    var r = await SendAsync(client, HttpMethod.Post, "/api/commands",
      Body(UniqueName(), TapStep(new() { ["holdMs"] = 800 }))).ConfigureAwait(false);

    r.Status.Should().Be(HttpStatusCode.Created, r.Text);
    r.Json.GetProperty("steps")[0].GetProperty("primitiveTap").GetProperty("holdMs").GetInt32().Should().Be(800);
  }

  [Fact]
  public async Task Post_FieldNamesInOtherLetterCase_Gives201() {
    using var app = CreateFactory();
    var client = AuthedClient(app);
    const string raw = """
      {"Name":"case-test-REPLACE","Steps":[{"Type":"PrimitiveTap","Order":0,"PrimitiveTap":{"DetectionTarget":{"ReferenceImageId":"exchange-button"}}}]}
      """;

    var r = await SendRawAsync(client, HttpMethod.Post, "/api/commands", raw.Replace("REPLACE", Guid.NewGuid().ToString("N"), StringComparison.Ordinal)).ConfigureAwait(false);

    r.Status.Should().Be(HttpStatusCode.Created, r.Text);
  }

  [Fact]
  public async Task Post_RootDetectionTargetAlias_Gives201() {
    using var app = CreateFactory();
    var client = AuthedClient(app);
    var body = Body(UniqueName(), TapStep());
    body["detectionTarget"] = new { referenceImageId = "exchange-button", confidence = 0.9 };

    var r = await SendAsync(client, HttpMethod.Post, "/api/commands", body).ConfigureAwait(false);

    r.Status.Should().Be(HttpStatusCode.Created, r.Text);
  }

  // ---- User Story 3: PATCH ----

  [Fact]
  public async Task Patch_UnknownStepField_Gives400WithPathAndHint() {
    using var app = CreateFactory();
    var client = AuthedClient(app);
    var id = await CreateValidAsync(client, UniqueName()).ConfigureAwait(false);

    var r = await SendAsync(client, HttpMethod.Patch, $"/api/commands/{id}",
      new { steps = new object[] { TapStep(new() { ["bogusField"] = 5 }) } }).ConfigureAwait(false);

    r.Status.Should().Be(HttpStatusCode.BadRequest, r.Text);
    ShouldRejectWith(r.Json, "steps[0].primitiveTap.bogusField");
  }

  [Fact]
  public async Task Patch_UnknownStepField_LeavesCommandUnchanged() {
    using var app = CreateFactory();
    var client = AuthedClient(app);
    var name = UniqueName();
    var id = await CreateValidAsync(client, name).ConfigureAwait(false);

    var r = await SendAsync(client, HttpMethod.Patch, $"/api/commands/{id}",
      new Dictionary<string, object?> { ["name"] = "renamed-" + name, ["steps"] = new object[] { TapStep(new() { ["bogusField"] = 5 }) } }).ConfigureAwait(false);

    r.Status.Should().Be(HttpStatusCode.BadRequest, r.Text);
    var after = await client.GetFromJsonAsync<JsonElement>(new Uri($"/api/commands/{id}", UriKind.Relative)).ConfigureAwait(false);
    after.GetProperty("name").GetString().Should().Be(name);
    after.GetProperty("steps").GetArrayLength().Should().Be(1);
  }

  [Fact]
  public async Task Patch_UnknownField_OnMissingId_Gives404() {
    using var app = CreateFactory();
    var client = AuthedClient(app);

    var r = await SendAsync(client, HttpMethod.Patch, "/api/commands/no-such-command",
      new { steps = new object[] { TapStep(new() { ["bogusField"] = 5 }) } }).ConfigureAwait(false);

    r.Status.Should().Be(HttpStatusCode.NotFound, r.Text);
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

  [Fact]
  public async Task Patch_EmptyBody_KeepsRecordedAnswer() {
    using var app = CreateFactory();
    var client = AuthedClient(app);
    var id = await CreateValidAsync(client, UniqueName()).ConfigureAwait(false);

    var r = await SendRawAsync(client, HttpMethod.Patch, $"/api/commands/{id}", string.Empty).ConfigureAwait(false);

    r.Status.Should().Be(HttpStatusCode.InternalServerError, r.Text);
    r.Json.GetProperty("error").GetProperty("code").GetString().Should().Be("internal_error");
  }

  [Fact]
  public async Task Patch_MalformedJson_KeepsRecordedAnswer() {
    using var app = CreateFactory();
    var client = AuthedClient(app);
    var id = await CreateValidAsync(client, UniqueName()).ConfigureAwait(false);

    var r = await SendRawAsync(client, HttpMethod.Patch, $"/api/commands/{id}", "{ not json").ConfigureAwait(false);

    r.Status.Should().Be(HttpStatusCode.InternalServerError, r.Text);
    r.Json.GetProperty("error").GetProperty("code").GetString().Should().Be("internal_error");
  }

  [Fact]
  public async Task Patch_MalformedJson_OnMissingId_KeepsRecordedAnswer() {
    using var app = CreateFactory();
    var client = AuthedClient(app);

    var r = await SendRawAsync(client, HttpMethod.Patch, "/api/commands/no-such-command", "{ not json").ConfigureAwait(false);

    r.Status.Should().Be(HttpStatusCode.InternalServerError, r.Text);
    r.Json.GetProperty("error").GetProperty("code").GetString().Should().Be("internal_error");
  }
}
