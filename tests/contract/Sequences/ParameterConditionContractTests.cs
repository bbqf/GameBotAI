using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

#pragma warning disable CA2007, CA1861

namespace GameBot.ContractTests.Sequences;

/// <summary>
/// Feature 193: the API contract of the parameter condition: save, update, patch, dryRun, read back with the
/// same JSON kind of value, and the 400 errors.
/// </summary>
public sealed class ParameterConditionContractTests {
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

  private static object HomeStep(string stepId, object? condition) => new {
    stepId,
    stepType = "Action",
    condition,
    primitiveAction = new { type = "go-to-home-screen", schemaVersion = "v1", payload = new { } }
  };

  private static readonly object[] Parameters = {
    new { name = "fighterBatch", type = "number" },
    new { name = "fighterTier", type = "text" },
    new {
      name = "tiers",
      type = "text",
      kind = "choice",
      multiple = true,
      options = new[] { new { label = "T1", value = "t1" }, new { label = "Max", value = "max" } }
    }
  };

  private static object Body(string name, object condition, bool dryRun = false, int version = 1) => new {
    name,
    version,
    dryRun,
    parameters = Parameters,
    steps = new[] {
      HomeStep("train", condition),
      HomeStep("tier", new {
        type = "all",
        children = new object[] { new { type = "parameter", name = "fighterTier", @operator = "equals", value = "max", negate = true } }
      })
    }
  };

  private static object Valid(object value) => new { type = "parameter", name = "fighterBatch", @operator = "equals", value };

  private static async Task<JsonElement> ReadAsync(HttpClient client, string id) =>
    JsonDocument.Parse(await client.GetStringAsync(new Uri($"/api/sequences/{id}", UriKind.Relative))).RootElement.Clone();

  private static async Task<string> CreateAsync(HttpClient client, object body) {
    var response = await client.PostAsJsonAsync("/api/sequences", body);
    var text = await response.Content.ReadAsStringAsync();
    response.StatusCode.Should().Be(HttpStatusCode.Created, text);
    return JsonDocument.Parse(text).RootElement.GetProperty("id").GetString()!;
  }

  [Fact]
  public async Task CreateSavesAndAReadKeepsTheJsonKindOfValue() {
    using var app = CreateFactory();
    var client = Client(app);
    var id = await CreateAsync(client, Body("pc-" + Guid.NewGuid().ToString("N"), Valid(0)));

    var read = await ReadAsync(client, id);
    var leaf = read.GetProperty("steps")[0].GetProperty("condition");
    leaf.GetProperty("type").GetString().Should().Be("parameter");
    leaf.GetProperty("name").GetString().Should().Be("fighterBatch");
    leaf.GetProperty("operator").GetString().Should().Be("equals");
    leaf.GetProperty("value").ValueKind.Should().Be(JsonValueKind.Number);
    leaf.GetProperty("value").GetRawText().Should().Be("0");
    leaf.GetProperty("negate").GetBoolean().Should().BeFalse();

    var child = read.GetProperty("steps")[1].GetProperty("condition").GetProperty("children")[0];
    child.GetProperty("value").ValueKind.Should().Be(JsonValueKind.String);
    child.GetProperty("value").GetString().Should().Be("max");
    child.GetProperty("negate").GetBoolean().Should().BeTrue();
  }

  [Fact]
  public async Task UpdateAndPatchAcceptTheCondition() {
    using var app = CreateFactory();
    var client = Client(app);
    var name = "pc-" + Guid.NewGuid().ToString("N");
    var id = await CreateAsync(client, Body(name, Valid(0)));

    var put = await client.PutAsJsonAsync(new Uri($"/api/sequences/{id}", UriKind.Relative), Body(name, Valid("5")));
    put.StatusCode.Should().Be(HttpStatusCode.OK, await put.Content.ReadAsStringAsync());
    var afterPut = (await ReadAsync(client, id)).GetProperty("steps")[0].GetProperty("condition").GetProperty("value");
    afterPut.ValueKind.Should().Be(JsonValueKind.String);
    afterPut.GetString().Should().Be("5");

    var patch = await client.PatchAsJsonAsync(new Uri($"/api/sequences/{id}", UriKind.Relative), Body(name, Valid(7), version: 2));
    patch.StatusCode.Should().Be(HttpStatusCode.OK, await patch.Content.ReadAsStringAsync());
    var afterPatch = (await ReadAsync(client, id)).GetProperty("steps")[0].GetProperty("condition").GetProperty("value");
    afterPatch.ValueKind.Should().Be(JsonValueKind.Number);
    afterPatch.GetInt32().Should().Be(7);
  }

  [Fact]
  public async Task ADryRunWithAValidConditionStoresNothing() {
    using var app = CreateFactory();
    var client = Client(app);
    var name = "pc-dry-" + Guid.NewGuid().ToString("N");

    var response = await client.PostAsJsonAsync("/api/sequences", Body(name, Valid(0), dryRun: true));
    var text = await response.Content.ReadAsStringAsync();

    response.StatusCode.Should().Be(HttpStatusCode.OK, text);
    var root = JsonDocument.Parse(text).RootElement;
    root.GetProperty("valid").GetBoolean().Should().BeTrue();
    root.GetProperty("dryRun").GetBoolean().Should().BeTrue();
    (await client.GetStringAsync(new Uri("/api/sequences", UriKind.Relative))).Should().NotContain(name);
  }

  public static TheoryData<object, string> BadConditions() => new() {
    { new { type = "parameter", name = "fighterBach", @operator = "equals", value = 0 }, "parameter 'fighterBach' is not a parameter of this sequence" },
    { new { type = "parameter", name = "fighterBatch", @operator = "contains", value = 0 }, "parameter operator must be one of equals, notEquals, greaterThan, lessThan." },
    { new { type = "parameter", name = "fighterBatch", @operator = "equals" }, "parameter condition requires value." },
    { new { type = "parameter", name = "", @operator = "equals", value = 0 }, "parameter condition requires name." },
    { new { type = "parameter", name = "fighterBatch", @operator = "equals", value = true }, "condition parameter value must be a string or a number." },
    { new { type = "parameter", name = "fighterBatch", @operator = "equals", value = "abc" }, "parameter 'fighterBatch' is a number parameter, and value 'abc' is not a whole number." },
    { new { type = "parameter", name = "tiers", @operator = "equals", value = "max" }, "parameter 'tiers' holds a list, and a parameter condition needs one value." }
  };

  [Theory]
  [MemberData(nameof(BadConditions))]
  public async Task CreateAndDryRunRefuseABadCondition(object condition, string expected) {
    using var app = CreateFactory();
    var client = Client(app);

    foreach (var dryRun in new[] { false, true }) {
      var response = await client.PostAsJsonAsync("/api/sequences", Body("pc-bad-" + Guid.NewGuid().ToString("N"), condition, dryRun));
      var text = await response.Content.ReadAsStringAsync();

      response.StatusCode.Should().Be(HttpStatusCode.BadRequest, text);
      text.Should().Contain(expected);
      text.Should().Contain("train");
    }
  }
}
