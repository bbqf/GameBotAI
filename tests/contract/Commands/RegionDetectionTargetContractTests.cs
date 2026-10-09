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

namespace GameBot.ContractTests.Commands;

/// <summary>
/// Feature 130: the optional <c>region</c> of a detection target is saved and read back in each place a
/// command holds one, and a bad region gives 400 that names every invalid field.
/// </summary>
public sealed class RegionDetectionTargetContractTests {
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

  private static object Target(object? region) => region is null
    ? new { referenceImageId = "exchange-button", confidence = 0.9 }
    : (object)new { referenceImageId = "exchange-button", confidence = 0.9, region };

  private static void ShouldBeRegion(JsonElement target) {
    var region = target.GetProperty("region");
    region.GetProperty("x").GetInt32().Should().Be(300);
    region.GetProperty("y").GetInt32().Should().Be(400);
    region.GetProperty("width").GetInt32().Should().Be(240);
    region.GetProperty("height").GetInt32().Should().Be(120);
  }

  private static async Task<JsonElement> CreateCommandAsync(System.Net.Http.HttpClient client, object payload) {
    var response = await client.PostAsJsonAsync("/api/commands", payload).ConfigureAwait(false);
    var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
    response.StatusCode.Should().Be(HttpStatusCode.Created, body);
    var id = JsonDocument.Parse(body).RootElement.GetProperty("id").GetString()!;
    var get = await client.GetAsync(new Uri($"/api/commands/{id}", UriKind.Relative)).ConfigureAwait(false);
    get.StatusCode.Should().Be(HttpStatusCode.OK);
    return JsonDocument.Parse(await get.Content.ReadAsStringAsync().ConfigureAwait(false)).RootElement.Clone();
  }

  [Fact]
  public async Task PrimitiveTapDetectionTargetKeepsItsRegion() {
    using var app = CreateFactory();
    var client = AuthedClient(app);

    var command = await CreateCommandAsync(client, new {
      name = $"tap-region-{Guid.NewGuid():N}",
      steps = new object[] { new { type = "PrimitiveTap", order = 0, primitiveTap = new { detectionTarget = Target(Region) } } }
    }).ConfigureAwait(false);

    ShouldBeRegion(command.GetProperty("steps")[0].GetProperty("primitiveTap").GetProperty("detectionTarget"));
  }

  [Fact]
  public async Task WaitForImageDetectionTargetKeepsItsRegion() {
    using var app = CreateFactory();
    var client = AuthedClient(app);

    var command = await CreateCommandAsync(client, new {
      name = $"wait-region-{Guid.NewGuid():N}",
      steps = new object[] { new { type = "WaitForImage", order = 0, waitForImage = new { detectionTarget = Target(Region), timeoutMs = 3000 } } }
    }).ConfigureAwait(false);

    ShouldBeRegion(command.GetProperty("steps")[0].GetProperty("waitForImage").GetProperty("detectionTarget"));
  }

  [Fact]
  public async Task ReadinessImageKeepsItsRegion() {
    using var app = CreateFactory();
    var client = AuthedClient(app);

    var command = await CreateCommandAsync(client, new {
      name = $"ready-region-{Guid.NewGuid():N}",
      steps = new object[] { new { type = "EnsureGameRunning", order = 0, ensureGameRunning = new { readinessImage = Target(Region), readinessTimeoutMs = 5000 } } }
    }).ConfigureAwait(false);

    ShouldBeRegion(command.GetProperty("steps")[0].GetProperty("ensureGameRunning").GetProperty("readinessImage"));
  }

  [Fact]
  public async Task CommandLevelDetectionKeepsItsRegion() {
    using var app = CreateFactory();
    var client = AuthedClient(app);

    var command = await CreateCommandAsync(client, new {
      name = $"detection-region-{Guid.NewGuid():N}",
      steps = new object[] { new { type = "KeyInput", order = 0, keyInput = new { key = "HOME" } } },
      detection = Target(Region)
    }).ConfigureAwait(false);

    ShouldBeRegion(command.GetProperty("detection"));
  }

  [Fact]
  public async Task PatchKeepsTheCommandLevelRegion() {
    using var app = CreateFactory();
    var client = AuthedClient(app);
    var created = await CreateCommandAsync(client, new {
      name = $"patch-region-{Guid.NewGuid():N}",
      steps = new object[] { new { type = "KeyInput", order = 0, keyInput = new { key = "HOME" } } },
      detection = Target(null)
    }).ConfigureAwait(false);
    var id = created.GetProperty("id").GetString()!;

    var patch = await client.PatchAsJsonAsync($"/api/commands/{id}", new { detection = Target(Region) }).ConfigureAwait(false);
    var body = await patch.Content.ReadAsStringAsync().ConfigureAwait(false);

    patch.StatusCode.Should().Be(HttpStatusCode.OK, body);
    ShouldBeRegion(JsonDocument.Parse(body).RootElement.GetProperty("detection"));
  }

  [Fact]
  public async Task TargetWithNoRegionHasNoRegionField() {
    using var app = CreateFactory();
    var client = AuthedClient(app);

    var command = await CreateCommandAsync(client, new {
      name = $"no-region-{Guid.NewGuid():N}",
      steps = new object[] { new { type = "PrimitiveTap", order = 0, primitiveTap = new { detectionTarget = Target(null) } } },
      detection = Target(null)
    }).ConfigureAwait(false);

    command.GetRawText().Should().NotContain("\"region\"");
  }

  // ---------- bad regions give 400 in each place ----------

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

  private static IEnumerable<(string Place, object Payload)> BadPayloads(object region) {
    var name = $"bad-region-{Guid.NewGuid():N}";
    yield return ("primitiveTap", new { name, steps = new object[] { new { type = "PrimitiveTap", order = 0, primitiveTap = new { detectionTarget = Target(region) } } } });
    yield return ("waitForImage", new { name, steps = new object[] { new { type = "WaitForImage", order = 0, waitForImage = new { detectionTarget = Target(region) } } } });
    yield return ("readinessImage", new { name, steps = new object[] { new { type = "EnsureGameRunning", order = 0, ensureGameRunning = new { readinessImage = Target(region) } } } });
    yield return ("detection", new { name, steps = new object[] { new { type = "KeyInput", order = 0, keyInput = new { key = "HOME" } } }, detection = Target(region) });
  }

  [Theory]
  [MemberData(nameof(BadRegions))]
  public async Task BadRegionGives400WithEveryInvalidFieldNamedInEachPlace(string caseName, object region, string[] expected) {
    using var app = CreateFactory();
    var client = AuthedClient(app);

    foreach (var (place, payload) in BadPayloads(region)) {
      var response = await client.PostAsJsonAsync("/api/commands", payload).ConfigureAwait(false);
      var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

      response.StatusCode.Should().Be(HttpStatusCode.BadRequest, $"{caseName} in {place}: {body}");
      foreach (var message in expected) body.Should().Contain(message, $"{caseName} in {place}");
    }

    var name = JsonSerializer.SerializeToElement(BadPayloads(region).First().Payload).GetProperty("name").GetString()!;
    var list = await client.GetStringAsync(new Uri("/api/commands", UriKind.Relative)).ConfigureAwait(false);
    list.Should().NotContain(name);
  }

  [Fact]
  public async Task BadRegionOnPatchGives400AndKeepsTheStoredCommand() {
    using var app = CreateFactory();
    var client = AuthedClient(app);
    var created = await CreateCommandAsync(client, new {
      name = $"patch-bad-{Guid.NewGuid():N}",
      steps = new object[] { new { type = "KeyInput", order = 0, keyInput = new { key = "HOME" } } },
      detection = Target(Region)
    }).ConfigureAwait(false);
    var id = created.GetProperty("id").GetString()!;

    var patch = await client.PatchAsJsonAsync($"/api/commands/{id}", new { detection = Target(new { x = 0, y = 0, width = 0, height = 1 }) }).ConfigureAwait(false);

    patch.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    var stored = await client.GetStringAsync(new Uri($"/api/commands/{id}", UriKind.Relative)).ConfigureAwait(false);
    ShouldBeRegion(JsonDocument.Parse(stored).RootElement.GetProperty("detection"));
  }

  [Theory]
  [InlineData("primitiveTap")]
  [InlineData("waitForImage")]
  public async Task BadRegionOnTheStepsEndpointGives400(string place) {
    using var app = CreateFactory();
    var client = AuthedClient(app);
    var bad = new { x = 0, y = 0, width = 0, height = 10 };
    object step = place == "primitiveTap"
      ? new { type = "PrimitiveTap", order = 0, primitiveTap = new { detectionTarget = Target(bad) } }
      : new { type = "WaitForImage", order = 0, waitForImage = new { detectionTarget = Target(bad) } };

    var response = await client.PostAsJsonAsync("/api/steps/execute", new { sessionId = "none", step }).ConfigureAwait(false);
    var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

    response.StatusCode.Should().Be(HttpStatusCode.BadRequest, body);
    body.Should().Contain("region.width must be greater than 0");
  }
}
