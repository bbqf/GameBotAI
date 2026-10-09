using System;
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
/// Feature 130: the <c>detectionTarget.region</c> of a <c>waitForImage</c> or <c>primitiveTap</c> sequence
/// step payload is saved and read back, and a bad region gives 400 that names every invalid field.
/// </summary>
public sealed class RegionPayloadContractTests {
  private static readonly object Region = new { x = 300, y = 400, width = 240, height = 120 };

  private static WebApplicationFactory<Program> CreateFactory() {
    Environment.SetEnvironmentVariable("GAMEBOT_USE_ADB", "false");
    Environment.SetEnvironmentVariable("GAMEBOT_DYNAMIC_PORT", "true");
    Environment.SetEnvironmentVariable("GAMEBOT_AUTH_TOKEN", "test-token");
    return new WebApplicationFactory<Program>();
  }

  private static System.Net.Http.HttpClient AuthedClient(WebApplicationFactory<Program> app) {
    var client = app.CreateClient();
    client.DefaultRequestHeaders.Add("Authorization", "Bearer test-token");
    return client;
  }

  private static object WaitStep(object? region) => new {
    stepId = "wait1",
    primitiveAction = new {
      type = "WaitForImage",
      schemaVersion = "v1",
      payload = new {
        timeoutMs = 2000,
        detectionTarget = region is null
          ? new { referenceImageId = "exchange-button", confidence = 0.9 }
          : (object)new { referenceImageId = "exchange-button", confidence = 0.9, region }
      }
    }
  };

  private static object TapStep(object? region) => new {
    stepId = "tap1",
    primitiveAction = new {
      type = "tap",
      schemaVersion = "v1",
      payload = new {
        x = 10,
        y = 10,
        detectionTarget = region is null
          ? new { referenceImageId = "exchange-button" }
          : (object)new { referenceImageId = "exchange-button", region }
      }
    }
  };

  private static object Payload(object step) => new { name = $"payload-region-{Guid.NewGuid():N}", version = 1, steps = new[] { step } };

  private static void ShouldBeRegion(JsonElement detectionTarget) {
    var region = detectionTarget.GetProperty("region");
    region.GetProperty("x").GetInt32().Should().Be(300);
    region.GetProperty("y").GetInt32().Should().Be(400);
    region.GetProperty("width").GetInt32().Should().Be(240);
    region.GetProperty("height").GetInt32().Should().Be(120);
  }

  private static async Task<JsonElement> CreateAndReadBackAsync(System.Net.Http.HttpClient client, object payload) {
    var response = await client.PostAsJsonAsync("/api/sequences", payload).ConfigureAwait(false);
    var created = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
    response.StatusCode.Should().Be(HttpStatusCode.Created, created);
    var id = JsonDocument.Parse(created).RootElement.GetProperty("id").GetString()!;
    var get = await client.GetAsync(new Uri($"/api/sequences/{id}", UriKind.Relative)).ConfigureAwait(false);
    get.StatusCode.Should().Be(HttpStatusCode.OK);
    return JsonDocument.Parse(await get.Content.ReadAsStringAsync().ConfigureAwait(false)).RootElement.Clone();
  }

  [Fact]
  public async Task WaitForImagePayloadKeepsItsRegion() {
    using var app = CreateFactory();
    var client = AuthedClient(app);

    var sequence = await CreateAndReadBackAsync(client, Payload(WaitStep(Region))).ConfigureAwait(false);

    ShouldBeRegion(sequence.GetProperty("steps")[0].GetProperty("primitiveAction").GetProperty("payload").GetProperty("detectionTarget"));
  }

  [Fact]
  public async Task PrimitiveTapPayloadKeepsItsRegion() {
    using var app = CreateFactory();
    var client = AuthedClient(app);

    var sequence = await CreateAndReadBackAsync(client, Payload(TapStep(Region))).ConfigureAwait(false);

    ShouldBeRegion(sequence.GetProperty("steps")[0].GetProperty("primitiveAction").GetProperty("payload").GetProperty("detectionTarget"));
  }

  [Fact]
  public async Task PayloadWithNoRegionHasNoRegionField() {
    using var app = CreateFactory();
    var client = AuthedClient(app);

    var sequence = await CreateAndReadBackAsync(client, Payload(WaitStep(null))).ConfigureAwait(false);

    sequence.GetRawText().Should().NotContain("\"region\"");
  }

  public static TheoryData<string, object, string[]> BadRegions() => new() {
    { "zero-width", new { x = 0, y = 0, width = 0, height = 10 }, new[] { "region.width must be greater than 0" } },
    { "negative-x", new { x = -1, y = 0, width = 10, height = 10 }, new[] { "region.x must be 0 or more" } },
    { "missing-height", new { x = 0, y = 0, width = 10 }, new[] { "region.height is required" } },
    {
      "every-field-bad",
      new { x = -1, y = -1, width = 0, height = 0 },
      new[] { "region.x must be 0 or more", "region.y must be 0 or more", "region.width must be greater than 0", "region.height must be greater than 0" }
    }
  };

  [Theory]
  [MemberData(nameof(BadRegions))]
  public async Task BadRegionInAWaitForImagePayloadGives400(string caseName, object region, string[] expected) {
    using var app = CreateFactory();
    var client = AuthedClient(app);

    var response = await client.PostAsJsonAsync("/api/sequences", Payload(WaitStep(region))).ConfigureAwait(false);
    var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

    response.StatusCode.Should().Be(HttpStatusCode.BadRequest, $"{caseName}: {body}");
    foreach (var message in expected) body.Should().Contain(message);
    body.Should().Contain("Step 'wait1'");
  }

  [Theory]
  [MemberData(nameof(BadRegions))]
  public async Task BadRegionInAPrimitiveTapPayloadGives400(string caseName, object region, string[] expected) {
    using var app = CreateFactory();
    var client = AuthedClient(app);

    var response = await client.PostAsJsonAsync("/api/sequences", Payload(TapStep(region))).ConfigureAwait(false);
    var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

    response.StatusCode.Should().Be(HttpStatusCode.BadRequest, $"{caseName}: {body}");
    foreach (var message in expected) body.Should().Contain(message);
    body.Should().Contain("Step 'tap1'");
  }

  [Fact]
  public async Task RegionFieldThatIsNotAWholeNumberGives400() {
    using var app = CreateFactory();
    var client = AuthedClient(app);

    var response = await client.PostAsJsonAsync("/api/sequences", Payload(WaitStep(new { x = "left", y = 0, width = 10, height = 10 }))).ConfigureAwait(false);
    var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

    response.StatusCode.Should().Be(HttpStatusCode.BadRequest, body);
    body.Should().Contain("region.x must be a whole number");
  }
}
