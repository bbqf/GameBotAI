using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

#pragma warning disable CA2007

namespace GameBot.ContractTests.Sequences;

/// <summary>
/// The API contract of the ocrText condition (issue #338). The central rule is 400, never 500: each bad field
/// gets a message that names it.
/// </summary>
public sealed class OcrTextConditionContractTests {
  private static readonly object Region = new { x = 10, y = 20, width = 100, height = 40 };

  private static WebApplicationFactory<Program> CreateFactory() {
    Environment.SetEnvironmentVariable("GAMEBOT_USE_ADB", "false");
    Environment.SetEnvironmentVariable("GAMEBOT_DYNAMIC_PORT", "true");
    Environment.SetEnvironmentVariable("GAMEBOT_AUTH_TOKEN", "test-token");
    return new WebApplicationFactory<Program>();
  }

  private static HttpClient Client(WebApplicationFactory<Program> app) {
    var client = app.CreateClient();
    client.DefaultRequestHeaders.Add("Authorization", "Bearer test-token");
    return client;
  }

  private static object Payload(object condition) => new {
    name = "ocrtext-" + Guid.NewGuid().ToString("N"),
    version = 1,
    steps = new[] {
      new {
        stepId = "guarded",
        primitiveAction = new { type = "tap", schemaVersion = "v1", payload = new { x = 5, y = 5 } },
        condition
      }
    }
  };

  private static async Task<string> ErrorsAsync(HttpResponseMessage response) {
    var content = await response.Content.ReadAsStringAsync();
    response.StatusCode.Should().Be(HttpStatusCode.BadRequest, content);
    return string.Join(" | ", JsonDocument.Parse(content).RootElement.GetProperty("errors").EnumerateArray().Select(e => e.GetString()));
  }

  [Fact]
  public async Task AMissingRegionNamesRegion() {
    using var app = CreateFactory();
    var response = await Client(app).PostAsJsonAsync("/api/sequences", Payload(new { type = "ocrText", expected = "0" }));

    (await ErrorsAsync(response)).Should().Contain("Step 'guarded' condition at $:").And.Contain("region");
  }

  [Theory]
  [InlineData(0, 10)]
  [InlineData(10, 0)]
  [InlineData(-3, 10)]
  public async Task ARegionWithNoAreaNamesRegion(int width, int height) {
    using var app = CreateFactory();
    var condition = new { type = "ocrText", region = new { x = 1, y = 1, width, height }, expected = "0" };

    var response = await Client(app).PostAsJsonAsync("/api/sequences", Payload(condition));

    (await ErrorsAsync(response)).Should().Contain("region");
  }

  [Fact]
  public async Task BothOrNeitherOfExpectedAndPatternNamesBoth() {
    using var app = CreateFactory();
    var client = Client(app);

    var both = await client.PostAsJsonAsync("/api/sequences", Payload(new { type = "ocrText", region = Region, expected = "0", pattern = "0" }));
    var neither = await client.PostAsJsonAsync("/api/sequences", Payload(new { type = "ocrText", region = Region }));

    (await ErrorsAsync(both)).Should().Contain("expected").And.Contain("pattern");
    (await ErrorsAsync(neither)).Should().Contain("expected").And.Contain("pattern");
  }

  [Fact]
  public async Task AnInvalidPatternNamesPattern() {
    using var app = CreateFactory();

    var response = await Client(app).PostAsJsonAsync("/api/sequences", Payload(new { type = "ocrText", region = Region, pattern = "(unclosed" }));

    (await ErrorsAsync(response)).Should().Contain("pattern");
  }

  [Fact]
  public async Task AnUnknownParserNamesParserAndTheSupportedParsers() {
    using var app = CreateFactory();

    var response = await Client(app).PostAsJsonAsync("/api/sequences", Payload(new { type = "ocrText", region = Region, expected = "0", parser = "mm:ss" }));

    (await ErrorsAsync(response)).Should().Contain("parser").And.Contain("hh:mm:ss");
  }

  [Fact]
  public async Task AValidConditionSavesAndReadsBack() {
    using var app = CreateFactory();
    var client = Client(app);

    var create = await client.PostAsJsonAsync("/api/sequences", Payload(new { type = "ocrText", region = Region, expected = "0", negate = true }));
    var body = await create.Content.ReadAsStringAsync();
    create.StatusCode.Should().Be(HttpStatusCode.Created, body);
    var id = JsonDocument.Parse(body).RootElement.GetProperty("id").GetString();

    var read = JsonDocument.Parse(await client.GetStringAsync(new Uri($"/api/sequences/{id}", UriKind.Relative)))
      .RootElement.GetProperty("steps")[0].GetProperty("condition");

    read.GetProperty("type").GetString().Should().Be("ocrText");
    read.GetProperty("expected").GetString().Should().Be("0");
    read.GetProperty("negate").GetBoolean().Should().BeTrue();
    read.GetProperty("region").GetProperty("width").GetInt32().Should().Be(100);
  }

  [Fact]
  public async Task AnUpperCaseParserIsStoredWithTheCanonicalName() {
    using var app = CreateFactory();
    var client = Client(app);

    var create = await client.PostAsJsonAsync("/api/sequences", Payload(new { type = "ocrText", region = Region, pattern = "^02:", parser = "HH:MM:SS" }));
    var body = await create.Content.ReadAsStringAsync();
    create.StatusCode.Should().Be(HttpStatusCode.Created, body);
    var id = JsonDocument.Parse(body).RootElement.GetProperty("id").GetString();

    var read = JsonDocument.Parse(await client.GetStringAsync(new Uri($"/api/sequences/{id}", UriKind.Relative)))
      .RootElement.GetProperty("steps")[0].GetProperty("condition");

    read.GetProperty("parser").GetString().Should().Be("hh:mm:ss");
    read.GetProperty("pattern").GetString().Should().Be("^02:");
  }

  // ── Feature 165: sequence parameters in pattern and expected ───────────────

  private static object ParamPayload(object condition, object[] parameters, bool dryRun = false) => new {
    name = "ocrparam-" + Guid.NewGuid().ToString("N"),
    version = 1,
    dryRun,
    parameters,
    steps = new[] {
      new {
        stepId = "s3",
        primitiveAction = new { type = "tap", schemaVersion = "v1", payload = new { x = 5, y = 5 } },
        condition
      }
    }
  };

  private static object Ocr(string? pattern = null, string? expected = null) => pattern is not null
      ? new { type = "ocrText", region = Region, pattern }
      : new { type = "ocrText", region = Region, expected };

  /// <summary>
  /// Sends <paramref name="body"/> with POST, or with PUT or PATCH to a sequence that was saved first.
  /// </summary>
  private static async Task<HttpResponseMessage> SaveAsync(HttpClient client, string method, Func<bool, object> body, bool dryRun) {
    if (method == "POST") return await client.PostAsJsonAsync("/api/sequences", body(dryRun));

    var create = await client.PostAsJsonAsync("/api/sequences", ParamPayload(Ocr(expected: "0"), Array.Empty<object>()));
    var created = await create.Content.ReadAsStringAsync();
    create.StatusCode.Should().Be(HttpStatusCode.Created, created);
    var id = JsonDocument.Parse(created).RootElement.GetProperty("id").GetString();
    return method == "PUT"
        ? await client.PutAsJsonAsync($"/api/sequences/{id}", body(dryRun))
        : await client.PatchAsJsonAsync($"/api/sequences/{id}", body(dryRun));
  }

  private static async Task<JsonElement> ParameterErrorAsync(HttpResponseMessage response) {
    var content = await response.Content.ReadAsStringAsync();
    response.StatusCode.Should().Be(HttpStatusCode.BadRequest, content);
    return JsonDocument.Parse(content).RootElement.GetProperty("details")[0];
  }

  private static System.Collections.Generic.IEnumerable<string> StringValues(JsonElement element) => element.ValueKind switch {
    JsonValueKind.String => new[] { element.GetString() ?? string.Empty },
    JsonValueKind.Object => element.EnumerateObject().SelectMany(p => StringValues(p.Value)),
    JsonValueKind.Array => element.EnumerateArray().SelectMany(StringValues),
    _ => Array.Empty<string>()
  };

  public static TheoryData<string, bool> SaveMethods() => new() {
    { "POST", false }, { "POST", true }, { "PUT", false }, { "PUT", true }, { "PATCH", false }, { "PATCH", true }
  };

  [Theory]
  [MemberData(nameof(SaveMethods))]
  public async Task AnUnknownNameInPatternIsUnresolvable(string method, bool dryRun) {
    using var app = CreateFactory();

    var response = await SaveAsync(Client(app), method,
        d => ParamPayload(Ocr("(?s)(Gathering.*){{{missing}}}"), Array.Empty<object>(), d), dryRun);

    var error = await ParameterErrorAsync(response);
    error.GetProperty("code").GetString().Should().Be("unresolvable_parameter_reference");
    error.GetProperty("fieldPath").GetString().Should().Be("condition.pattern");
    error.GetProperty("parameterName").GetString().Should().Be("missing");
    error.GetProperty("message").GetString().Should().Be("Step 's3': 'missing' is not declared here and is not a queue built-in.");
  }

  [Theory]
  [MemberData(nameof(SaveMethods))]
  public async Task AnUnknownNameInExpectedIsUnresolvable(string method, bool dryRun) {
    using var app = CreateFactory();

    var response = await SaveAsync(Client(app), method,
        d => ParamPayload(Ocr(expected: "Level {{level}}"), Array.Empty<object>(), d), dryRun);

    var error = await ParameterErrorAsync(response);
    error.GetProperty("code").GetString().Should().Be("unresolvable_parameter_reference");
    error.GetProperty("fieldPath").GetString().Should().Be("condition.expected");
  }

  [Fact]
  public async Task AnUnknownNameInACompositeChildHasTheChildPath() {
    using var app = CreateFactory();
    var composite = new { type = "any", children = new[] { Ocr(expected: "0"), Ocr("{{missing}}") } };

    var response = await Client(app).PostAsJsonAsync("/api/sequences", ParamPayload(composite, Array.Empty<object>()));

    (await ParameterErrorAsync(response)).GetProperty("fieldPath").GetString().Should().Be("condition.children[1].pattern");
  }

  [Theory]
  [InlineData(false)]
  [InlineData(true)]
  public async Task DoubleBraceDigitsWithNoParameterIsUnresolvable(bool dryRun) {
    using var app = CreateFactory();

    var response = await Client(app).PostAsJsonAsync("/api/sequences",
        ParamPayload(Ocr("(Gathering.*){{3}}"), Array.Empty<object>(), dryRun));

    var error = await ParameterErrorAsync(response);
    error.GetProperty("code").GetString().Should().Be("unresolvable_parameter_reference");
    error.GetProperty("parameterName").GetString().Should().Be("3");
    error.GetProperty("fieldPath").GetString().Should().Be("condition.pattern");
  }

  [Theory]
  [MemberData(nameof(SaveMethods))]
  public async Task ABadPatternWithTheDefaultsIsInvalidResolvedPattern(string method, bool dryRun) {
    using var app = CreateFactory();
    var parameters = new object[] { new { name = "group", type = "text", @default = "(" } };

    var response = await SaveAsync(Client(app), method, d => ParamPayload(Ocr("{{group}}abc"), parameters, d), dryRun);

    var error = await ParameterErrorAsync(response);
    error.GetProperty("code").GetString().Should().Be("invalid_resolved_pattern");
    error.GetProperty("fieldPath").GetString().Should().Be("condition.pattern");
    error.GetProperty("parameterName").GetString().Should().Be("group");
    error.GetProperty("message").GetString().Should()
        .StartWith("Step 's3': 'condition.pattern' with the default values gives the pattern '(abc', which is not a valid regular expression: ")
        .And.EndWith(". Change the default value or the pattern.");
  }

  [Theory]
  [InlineData(false)]
  [InlineData(true)]
  public async Task APlaceholderInACharacterClassWithNoDefaultSaves(bool dryRun) {
    using var app = CreateFactory();
    var parameters = new object[] { new { name = "chars", type = "text" } };

    var response = await Client(app).PostAsJsonAsync("/api/sequences", ParamPayload(Ocr("[{{chars}}]"), parameters, dryRun));

    var body = await response.Content.ReadAsStringAsync();
    response.StatusCode.Should().Be(dryRun ? HttpStatusCode.OK : HttpStatusCode.Created, body);
  }

  [Theory]
  [InlineData(false)]
  [InlineData(true)]
  public async Task TheQuickstartSequenceSaves(bool dryRun) {
    using var app = CreateFactory();
    var parameters = new object[] { new { name = "marchTarget", type = "text", @default = "3" } };

    var response = await Client(app).PostAsJsonAsync("/api/sequences",
        ParamPayload(Ocr("(?s)(Gathering.*){{{marchTarget}}}"), parameters, dryRun));

    var body = await response.Content.ReadAsStringAsync();
    response.StatusCode.Should().Be(dryRun ? HttpStatusCode.OK : HttpStatusCode.Created, body);
  }

  [Fact]
  public async Task APatternWithNoPlaceholderKeepsTheExistingError() {
    using var app = CreateFactory();

    var response = await Client(app).PostAsJsonAsync("/api/sequences", ParamPayload(Ocr("(abc"), Array.Empty<object>()));

    (await ErrorsAsync(response)).Should().Contain("ocrText pattern is not a valid regular expression");
  }

  [Fact]
  public async Task ADryRunExecutionWithABadValueFailsTheStepWithTheRunMessage() {
    using var app = CreateFactory();
    var client = Client(app);
    var parameters = new object[] { new { name = "countPattern", type = "text" } };
    var create = await client.PostAsJsonAsync("/api/sequences", ParamPayload(Ocr("{{countPattern}}"), parameters));
    var created = await create.Content.ReadAsStringAsync();
    create.StatusCode.Should().Be(HttpStatusCode.Created, created);
    var id = JsonDocument.Parse(created).RootElement.GetProperty("id").GetString();

    var execute = await client.PostAsJsonAsync($"/api/sequences/{id}/execute", new {
      dryRun = true,
      parameters = new[] { new { name = "countPattern", value = "(" } }
    });

    var body = await execute.Content.ReadAsStringAsync();
    execute.StatusCode.Should().Be(HttpStatusCode.OK, body);
    var root = JsonDocument.Parse(body).RootElement;
    root.GetProperty("status").GetString().Should().Be("Failed");
    string.Join("\n", StringValues(root)).Should().Contain("Step 's3': field 'condition.pattern' resolved to the pattern '(', which is not a valid regular expression. "
        + "Change the value of 'countPattern' or the pattern.");
    body.Should().NotContain("invalid_resolved_pattern");
  }
}
