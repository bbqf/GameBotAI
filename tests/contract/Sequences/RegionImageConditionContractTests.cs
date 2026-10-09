using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

#pragma warning disable CA1861, CA1062

namespace GameBot.ContractTests.Sequences;

/// <summary>
/// Feature 130: the optional <c>region</c> of an <c>imageVisible</c> condition is saved and read back in each
/// condition position, and a bad region gives 400 that names every invalid field. Nothing is stored.
/// </summary>
public sealed class RegionImageConditionContractTests {
  private const string OneByOnePngBase64 =
    "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO2n5u4AAAAASUVORK5CYII=";

  private static readonly object Region = new { x = 0, y = 400, width = 540, height = 120 };

  private static WebApplicationFactory<Program> CreateFactory() {
    Environment.SetEnvironmentVariable("GAMEBOT_USE_ADB", "false");
    Environment.SetEnvironmentVariable("GAMEBOT_DYNAMIC_PORT", "true");
    Environment.SetEnvironmentVariable("GAMEBOT_AUTH_TOKEN", "test-token");
    return new WebApplicationFactory<Program>();
  }

  private static async Task<System.Net.Http.HttpClient> ClientAsync(WebApplicationFactory<Program> app) {
    var client = app.CreateClient();
    client.DefaultRequestHeaders.Add("Authorization", "Bearer test-token");
    foreach (var id in new[] { "region-a", "region-b" }) {
      var response = await client.PostAsJsonAsync(new Uri("/api/images", UriKind.Relative), new { id, data = OneByOnePngBase64 }).ConfigureAwait(false);
      response.StatusCode.Should().BeOneOf(HttpStatusCode.Created, HttpStatusCode.OK, HttpStatusCode.Conflict);
    }
    return client;
  }

  private static object Tap(string stepId, object? condition = null) => condition is null
    ? new { stepId, primitiveAction = new { type = "tap", schemaVersion = "v1", payload = new { x = 50, y = 50 } } }
    : (object)new { stepId, primitiveAction = new { type = "tap", schemaVersion = "v1", payload = new { x = 50, y = 50 } }, condition };

  private static object Image(object? region = null) => region is null
    ? new { type = "imageVisible", imageId = "region-a", minSimilarity = 0.9 }
    : (object)new { type = "imageVisible", imageId = "region-a", minSimilarity = 0.9, region };

  private static object Payload(string label, params object[] steps) => new {
    name = $"{label}-{Guid.NewGuid():N}",
    version = 1,
    steps
  };

  private static int CountRegions(JsonElement element) {
    var count = 0;
    switch (element.ValueKind) {
      case JsonValueKind.Object:
        foreach (var property in element.EnumerateObject()) {
          if (property.Name == "region" && property.Value.ValueKind == JsonValueKind.Object) {
            count++;
            property.Value.GetProperty("x").GetInt32().Should().Be(0);
            property.Value.GetProperty("y").GetInt32().Should().Be(400);
            property.Value.GetProperty("width").GetInt32().Should().Be(540);
            property.Value.GetProperty("height").GetInt32().Should().Be(120);
          }
          else {
            count += CountRegions(property.Value);
          }
        }
        break;
      case JsonValueKind.Array:
        foreach (var item in element.EnumerateArray()) count += CountRegions(item);
        break;
    }
    return count;
  }

  private static async Task<(string Id, string Body)> CreateAndReadBackAsync(System.Net.Http.HttpClient client, object payload) {
    var response = await client.PostAsJsonAsync("/api/sequences", payload).ConfigureAwait(false);
    var created = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
    response.StatusCode.Should().Be(HttpStatusCode.Created, created);
    var id = JsonDocument.Parse(created).RootElement.GetProperty("id").GetString()!;
    var get = await client.GetAsync(new Uri($"/api/sequences/{id}", UriKind.Relative)).ConfigureAwait(false);
    get.StatusCode.Should().Be(HttpStatusCode.OK);
    return (id, await get.Content.ReadAsStringAsync().ConfigureAwait(false));
  }

  [Fact]
  public async Task TopLevelConditionKeepsItsRegion() {
    using var app = CreateFactory();
    var client = await ClientAsync(app).ConfigureAwait(false);

    var (_, body) = await CreateAndReadBackAsync(client, Payload("top", Tap("s1", Image(Region)))).ConfigureAwait(false);

    CountRegions(JsonDocument.Parse(body).RootElement).Should().Be(1);
  }

  [Theory]
  [InlineData("all")]
  [InlineData("any")]
  [InlineData("none")]
  public async Task CompositeChildrenKeepTheirOwnRegion(string rule) {
    using var app = CreateFactory();
    var client = await ClientAsync(app).ConfigureAwait(false);
    var composite = new { type = rule, children = new object[] { Image(Region), Image() } };

    var (_, body) = await CreateAndReadBackAsync(client, Payload($"composite-{rule}", Tap("s1", composite))).ConfigureAwait(false);

    // Only the first child holds a region. The second child has no region field.
    CountRegions(JsonDocument.Parse(body).RootElement).Should().Be(1);
  }

  [Fact]
  public async Task IfConditionKeepsItsRegion() {
    using var app = CreateFactory();
    var client = await ClientAsync(app).ConfigureAwait(false);
    var ifStep = new {
      stepId = "if1",
      stepType = "If",
      @if = new { condition = Image(Region) },
      body = new[] { Tap("then1") }
    };

    var (_, body) = await CreateAndReadBackAsync(client, Payload("if", ifStep)).ConfigureAwait(false);

    CountRegions(JsonDocument.Parse(body).RootElement).Should().Be(1);
  }

  [Fact]
  public async Task LoopConditionAndBreakConditionKeepTheirRegion() {
    using var app = CreateFactory();
    var client = await ClientAsync(app).ConfigureAwait(false);
    var loop = new {
      stepId = "loop1",
      stepType = "Loop",
      loop = new { loopType = "while", condition = Image(Region), maxIterations = 3 },
      body = new object[] {
        Tap("in1"),
        new { stepId = "brk1", stepType = "Break", breakCondition = Image(Region) }
      }
    };

    var (_, body) = await CreateAndReadBackAsync(client, Payload("loop", loop)).ConfigureAwait(false);

    CountRegions(JsonDocument.Parse(body).RootElement).Should().Be(2);
  }

  [Fact]
  public async Task ConditionWithNoRegionHasNoRegionField() {
    using var app = CreateFactory();
    var client = await ClientAsync(app).ConfigureAwait(false);

    var (_, body) = await CreateAndReadBackAsync(client, Payload("none", Tap("s1", Image()))).ConfigureAwait(false);

    body.Should().NotContain("\"region\"");
  }

  [Fact]
  public async Task PutKeepsTheRegion() {
    using var app = CreateFactory();
    var client = await ClientAsync(app).ConfigureAwait(false);
    var (id, _) = await CreateAndReadBackAsync(client, Payload("put", Tap("s1", Image()))).ConfigureAwait(false);

    var put = await client.PutAsJsonAsync($"/api/sequences/{id}", Payload("put-updated", Tap("s1", Image(Region)))).ConfigureAwait(false);
    var putBody = await put.Content.ReadAsStringAsync().ConfigureAwait(false);

    put.StatusCode.Should().Be(HttpStatusCode.OK, putBody);
    CountRegions(JsonDocument.Parse(putBody).RootElement).Should().Be(1);
  }

  // ---------- US4: a bad region gives 400 and nothing is stored ----------

  public static TheoryData<string, object, string[]> BadRegions() => new() {
    { "zero-width", new { x = 0, y = 0, width = 0, height = 10 }, new[] { "region.width must be greater than 0" } },
    { "negative-height", new { x = 0, y = 0, width = 10, height = -1 }, new[] { "region.height must be greater than 0" } },
    { "negative-x", new { x = -1, y = 0, width = 10, height = 10 }, new[] { "region.x must be 0 or more" } },
    { "negative-y", new { y = -2, x = 0, width = 10, height = 10 }, new[] { "region.y must be 0 or more" } },
    { "missing-height", new { x = 0, y = 0, width = 10 }, new[] { "region.height is required" } },
    {
      "every-field-bad",
      new { x = -1, y = -1, width = 0, height = 0 },
      new[] { "region.x must be 0 or more", "region.y must be 0 or more", "region.width must be greater than 0", "region.height must be greater than 0" }
    }
  };

  [Theory]
  [MemberData(nameof(BadRegions))]
  public async Task BadRegionOnATopLevelConditionGives400AndStoresNothing(string caseName, object region, string[] expected) {
    using var app = CreateFactory();
    var client = await ClientAsync(app).ConfigureAwait(false);
    var payload = Payload($"bad-{caseName}", Tap("s1", Image(region)));
    var name = JsonSerializer.SerializeToElement(payload).GetProperty("name").GetString()!;

    var response = await client.PostAsJsonAsync("/api/sequences", payload).ConfigureAwait(false);
    var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

    response.StatusCode.Should().Be(HttpStatusCode.BadRequest, body);
    foreach (var message in expected) body.Should().Contain(message);

    var list = await client.GetStringAsync(new Uri("/api/sequences", UriKind.Relative)).ConfigureAwait(false);
    list.Should().NotContain(name);
  }

  [Fact]
  public async Task BadRegionInsideACompositeChildGives400() {
    using var app = CreateFactory();
    var client = await ClientAsync(app).ConfigureAwait(false);
    var composite = new { type = "all", children = new object[] { Image(), Image(new { x = 0, y = 0, width = 0, height = 5 }) } };

    var response = await client.PostAsJsonAsync("/api/sequences", Payload("bad-child", Tap("s1", composite))).ConfigureAwait(false);
    var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

    response.StatusCode.Should().Be(HttpStatusCode.BadRequest, body);
    body.Should().Contain("region.width must be greater than 0");
    body.Should().Contain("children[1]");
  }

  [Fact]
  public async Task BadRegionInAnIfLoopAndBreakConditionGives400() {
    using var app = CreateFactory();
    var client = await ClientAsync(app).ConfigureAwait(false);
    var bad = Image(new { x = 0, y = 0, width = 0, height = 5 });
    var ifStep = new { stepId = "if1", stepType = "If", @if = new { condition = bad }, body = new[] { Tap("then1") } };
    var loop = new {
      stepId = "loop1",
      stepType = "Loop",
      loop = new { loopType = "while", condition = bad, maxIterations = 3 },
      body = new object[] { Tap("in1"), new { stepId = "brk1", stepType = "Break", breakCondition = bad } }
    };

    var response = await client.PostAsJsonAsync("/api/sequences", Payload("bad-positions", ifStep, loop)).ConfigureAwait(false);
    var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

    response.StatusCode.Should().Be(HttpStatusCode.BadRequest, body);
    body.Should().Contain("Step 'if1'").And.Contain("Step 'loop1'").And.Contain("Step 'brk1'");
  }

  [Fact]
  public async Task BadRegionOnPutGives400AndKeepsTheStoredSequence() {
    using var app = CreateFactory();
    var client = await ClientAsync(app).ConfigureAwait(false);
    var (id, _) = await CreateAndReadBackAsync(client, Payload("put-bad", Tap("s1", Image(Region)))).ConfigureAwait(false);

    var put = await client.PutAsJsonAsync($"/api/sequences/{id}", Payload("put-bad-2", Tap("s1", Image(new { x = -1, y = 0, width = 5, height = 5 })))).ConfigureAwait(false);

    put.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    var stored = await client.GetStringAsync(new Uri($"/api/sequences/{id}", UriKind.Relative)).ConfigureAwait(false);
    CountRegions(JsonDocument.Parse(stored).RootElement).Should().Be(1);
  }

  [Fact]
  public async Task SameBadRegionGivesTheSameMessageInEachPosition() {
    using var app = CreateFactory();
    var client = await ClientAsync(app).ConfigureAwait(false);
    var bad = new { x = 0, y = 0, width = 0, height = 5 };
    var messages = new List<string>();

    foreach (var step in new object[] {
      Tap("s1", Image(bad)),
      Tap("s1", new { type = "any", children = new object[] { Image(bad) } })
    }) {
      var response = await client.PostAsJsonAsync("/api/sequences", Payload("same", step)).ConfigureAwait(false);
      response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
      messages.Add(await response.Content.ReadAsStringAsync().ConfigureAwait(false));
    }

    messages.Should().OnlyContain(m => m.Contains("region.width must be greater than 0"));
  }
}
