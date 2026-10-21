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
}
