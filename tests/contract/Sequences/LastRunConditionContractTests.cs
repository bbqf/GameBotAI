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
/// The API contract of the <c>lastRun</c> condition (feature 105, User Story 3). The central rule is
/// <b>400, never 500</b>: each bad field gets a message that names it.
/// </summary>
public sealed class LastRunConditionContractTests {
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

  private static object Tap(string stepId, object? condition = null) => condition is null
    ? new { stepId, primitiveAction = new { type = "tap", schemaVersion = "v1", payload = new { x = 5, y = 5 } } }
    : (object)new { stepId, primitiveAction = new { type = "tap", schemaVersion = "v1", payload = new { x = 5, y = 5 } }, condition };

  private static object Payload(object condition, bool dryRun = false) => new {
    name = "lastrun-" + Guid.NewGuid().ToString("N"),
    version = 1,
    dryRun,
    steps = new[] { Tap("guarded", condition) }
  };

  private static async Task<string> CreateAsync(HttpClient client, object condition) {
    var response = await client.PostAsJsonAsync("/api/sequences", Payload(condition));
    var body = await response.Content.ReadAsStringAsync();
    response.StatusCode.Should().Be(HttpStatusCode.Created, body);
    return JsonDocument.Parse(body).RootElement.GetProperty("id").GetString()!;
  }

  public static TheoryData<string, object, string> BadConditions() => new() {
    { "no-sequence", new { type = "lastRun", status = "success", since = "11:00" }, "lastRun condition requires sequence ('self' or a sequence id)." },
    { "status-failed", new { type = "lastRun", sequence = "self", status = "failed", since = "11:00" }, "lastRun status must be one of success|failure|cancelled." },
    { "both", new { type = "lastRun", sequence = "self", status = "success", since = "11:00", within = "01:00:00" }, "lastRun condition accepts only one of since or within, not both." },
    { "neither", new { type = "lastRun", sequence = "self", status = "success" }, "lastRun condition requires one of since or within." },
    { "since-9", new { type = "lastRun", sequence = "self", status = "success", since = "9:00" }, "lastRun since must be a time of day in HH:mm format (00:00 to 23:59)." },
    { "since-24", new { type = "lastRun", sequence = "self", status = "success", since = "24:00" }, "lastRun since must be a time of day in HH:mm format (00:00 to 23:59)." },
    { "within-zero", new { type = "lastRun", sequence = "self", status = "success", within = "00:00:00" }, "lastRun within must be a duration more than zero and not more than 366 days, in hh:mm:ss or d.hh:mm:ss format." },
    { "within-400d", new { type = "lastRun", sequence = "self", status = "success", within = "400.00:00:00" }, "lastRun within must be a duration more than zero and not more than 366 days, in hh:mm:ss or d.hh:mm:ss format." },
    { "within-abc", new { type = "lastRun", sequence = "self", status = "success", within = "abc" }, "lastRun within must be a duration more than zero and not more than 366 days, in hh:mm:ss or d.hh:mm:ss format." }
  };

  private static async Task AssertBadRequestAsync(HttpResponseMessage response, string caseName, string tail) {
    var content = await response.Content.ReadAsStringAsync();
    response.StatusCode.Should().Be(HttpStatusCode.BadRequest, "case '{0}' returned {1}: {2}", caseName, response.StatusCode, content);
    var errors = JsonDocument.Parse(content).RootElement.GetProperty("errors").EnumerateArray()
      .Select(e => e.GetString()).ToList();
    errors.Should().Contain("Step 'guarded' condition at $: " + tail, caseName);
  }

  [Theory]
  [MemberData(nameof(BadConditions))]
  public async Task CreateRejectsABadLastRunWithFourHundred(string caseName, object condition, string tail) {
    using var app = CreateFactory();
    var client = Client(app);

    await AssertBadRequestAsync(await client.PostAsJsonAsync("/api/sequences", Payload(condition)), caseName, tail);
    await AssertBadRequestAsync(await client.PostAsJsonAsync("/api/sequences", Payload(condition, dryRun: true)), caseName, tail);
  }

  [Theory]
  [MemberData(nameof(BadConditions))]
  public async Task UpdateAndPatchRejectABadLastRunWithFourHundred(string caseName, object condition, string tail) {
    using var app = CreateFactory();
    var client = Client(app);
    var id = await CreateAsync(client, new { type = "lastRun", sequence = "self", status = "success", since = "11:00" });

    await AssertBadRequestAsync(await client.PutAsJsonAsync($"/api/sequences/{id}", Payload(condition)), caseName, tail);
    await AssertBadRequestAsync(await client.PatchAsJsonAsync($"/api/sequences/{id}", Payload(condition)), caseName, tail);
  }

  [Fact]
  public async Task ADryRunWithACorrectLastRunIsValid() {
    using var app = CreateFactory();
    var client = Client(app);

    var response = await client.PostAsJsonAsync("/api/sequences",
      Payload(new { type = "lastRun", sequence = "self", status = "success", since = "11:00" }, dryRun: true));
    var body = await response.Content.ReadAsStringAsync();

    response.StatusCode.Should().Be(HttpStatusCode.OK, body);
    JsonDocument.Parse(body).RootElement.GetProperty("valid").GetBoolean().Should().BeTrue();
  }

  [Theory]
  [InlineData("since", "11:00")]
  [InlineData("within", "24:00:00")]
  public async Task ASavedConditionReadsBackWithTheSameText(string field, string value) {
    using var app = CreateFactory();
    var client = Client(app);
    var condition = field == "since"
      ? (object)new { type = "lastRun", sequence = "self", status = "Success", since = value }
      : new { type = "lastRun", sequence = "seq-daily-train", status = "failure", within = value, negate = true };
    var id = await CreateAsync(client, condition);

    var read = JsonDocument.Parse(await client.GetStringAsync(new Uri($"/api/sequences/{id}", UriKind.Relative)))
      .RootElement.GetProperty("steps")[0].GetProperty("condition");

    read.GetProperty("type").GetString().Should().Be("lastRun");
    read.GetProperty(field).GetString().Should().Be(value);
    read.TryGetProperty(field == "since" ? "within" : "since", out _).Should().BeFalse("the field that is not set is left out");
    if (field == "since") {
      read.GetProperty("sequence").GetString().Should().Be("self");
      read.GetProperty("status").GetString().Should().Be("Success");
      read.GetProperty("negate").GetBoolean().Should().BeFalse();
    }
    else {
      read.GetProperty("sequence").GetString().Should().Be("seq-daily-train");
      read.GetProperty("negate").GetBoolean().Should().BeTrue();
    }
  }

  [Fact]
  public async Task ALastRunInACompositeSavesAndReadsBack() {
    using var app = CreateFactory();
    var client = Client(app);
    var id = await CreateAsync(client, new {
      type = "none",
      children = new object[] {
        new { type = "lastRun", sequence = "self", status = "success", since = "04:00" },
        new { type = "lastRun", sequence = "other", status = "cancelled", within = "1.00:00:00" }
      }
    });

    var children = JsonDocument.Parse(await client.GetStringAsync(new Uri($"/api/sequences/{id}", UriKind.Relative)))
      .RootElement.GetProperty("steps")[0].GetProperty("condition").GetProperty("children");

    children.GetArrayLength().Should().Be(2);
    children[0].GetProperty("since").GetString().Should().Be("04:00");
    children[1].GetProperty("within").GetString().Should().Be("1.00:00:00");
  }

  [Fact]
  public async Task ABadLastRunInsideACompositeNamesItsPath() {
    using var app = CreateFactory();
    var client = Client(app);

    var response = await client.PostAsJsonAsync("/api/sequences", Payload(new {
      type = "all",
      children = new object[] { new { type = "lastRun", sequence = "self", status = "success", since = "9:00" } }
    }));
    var content = await response.Content.ReadAsStringAsync();

    response.StatusCode.Should().Be(HttpStatusCode.BadRequest, content);
    content.Should().Contain("condition at $.children[0]: lastRun since must be");
  }
}
