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
/// Feature 180: an image list in an <c>imageVisible</c> condition and in a WaitForImage detection target.
/// The save accepts a written list and a list placeholder, refuses a bad list, and refuses a multi-value
/// parameter in a field that needs one value.
/// </summary>
public sealed class ImageListContractTests {
  private const string OneByOnePngBase64 =
    "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO2n5u4AAAAASUVORK5CYII=";

  private static WebApplicationFactory<Program> CreateFactory() {
    Environment.SetEnvironmentVariable("GAMEBOT_USE_ADB", "false");
    Environment.SetEnvironmentVariable("GAMEBOT_DYNAMIC_PORT", "true");
    Environment.SetEnvironmentVariable("GAMEBOT_AUTH_TOKEN", "test-token");
    return new WebApplicationFactory<Program>();
  }

  private static async Task<System.Net.Http.HttpClient> ClientAsync(WebApplicationFactory<Program> app) {
    var client = app.CreateClient();
    client.DefaultRequestHeaders.Add("Authorization", "Bearer test-token");
    foreach (var id in new[] { "list-a", "list-b" }) {
      var response = await client.PostAsJsonAsync(new Uri("/api/images", UriKind.Relative), new { id, data = OneByOnePngBase64 }).ConfigureAwait(false);
      response.StatusCode.Should().BeOneOf(HttpStatusCode.Created, HttpStatusCode.OK, HttpStatusCode.Conflict);
    }

    return client;
  }

  private static readonly object[] QuestsDeclaration = {
    new { name = "quests", type = "text", kind = "image", multiple = true, allowedImages = new[] { "list-a", "list-b" } }
  };

  private static object Tap(string stepId, object condition) => new {
    stepId,
    primitiveAction = new { type = "tap", schemaVersion = "v1", payload = new { x = 50, y = 50 } },
    condition
  };

  private static object SequencePayload(object condition) => new {
    name = $"image-list-{Guid.NewGuid():N}",
    version = 1,
    parameters = QuestsDeclaration,
    steps = new[] { Tap("tap", condition) }
  };

  private static async Task<(HttpStatusCode Status, JsonElement Body)> PostAsync(System.Net.Http.HttpClient client, string url, object payload) {
    var response = await client.PostAsJsonAsync(url, payload).ConfigureAwait(false);
    var text = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
    return (response.StatusCode, JsonDocument.Parse(text).RootElement.Clone());
  }

  // ---------- imageVisible ----------

  [Fact]
  public async Task WrittenListRoundTripsAndANoListConditionKeepsItsForm() {
    using var app = CreateFactory();
    var client = await ClientAsync(app).ConfigureAwait(false);

    var (status, body) = await PostAsync(client, "/api/sequences",
      SequencePayload(new { type = "imageVisible", imageIds = new[] { "list-a", "list-b", "list-a" }, match = "any" })).ConfigureAwait(false);
    status.Should().Be(HttpStatusCode.Created, body.ToString());
    var condition = body.GetProperty("steps")[0].GetProperty("condition");
    condition.GetProperty("imageIds").EnumerateArray().Select(e => e.GetString()).Should().Equal("list-a", "list-b");
    condition.GetProperty("match").GetString().Should().Be("any");
    condition.TryGetProperty("imageId", out _).Should().BeFalse();

    var (oneStatus, oneBody) = await PostAsync(client, "/api/sequences",
      SequencePayload(new { type = "imageVisible", imageId = "list-a" })).ConfigureAwait(false);
    oneStatus.Should().Be(HttpStatusCode.Created, oneBody.ToString());
    var one = oneBody.GetProperty("steps")[0].GetProperty("condition");
    one.GetProperty("imageId").GetString().Should().Be("list-a");
    one.TryGetProperty("imageIds", out _).Should().BeFalse();
    one.TryGetProperty("match", out _).Should().BeFalse();
  }

  [Fact]
  public async Task ListPlaceholderIsAcceptedWithTheSkippedStaticCheckWarning() {
    using var app = CreateFactory();
    var client = await ClientAsync(app).ConfigureAwait(false);

    var (status, body) = await PostAsync(client, "/api/sequences",
      SequencePayload(new { type = "imageVisible", imageId = "{{quests}}" })).ConfigureAwait(false);

    status.Should().Be(HttpStatusCode.Created, body.ToString());
    body.GetProperty("warnings").EnumerateArray()
      .Should().Contain(w => w.GetProperty("code").GetString() == "static_check_skipped"
        && w.GetProperty("fieldPath").GetString() == "condition.imageId");
  }

  [Theory]
  [InlineData("both")]
  [InlineData("match")]
  [InlineData("empty")]
  [InlineData("missing")]
  public async Task BadWrittenListIsRefused(string kind) {
    using var app = CreateFactory();
    var client = await ClientAsync(app).ConfigureAwait(false);
    object condition = kind switch {
      "both" => new { type = "imageVisible", imageId = "list-a", imageIds = new[] { "list-b" } },
      "match" => new { type = "imageVisible", imageIds = new[] { "list-a" }, match = "all" },
      "empty" => new { type = "imageVisible", imageIds = new[] { "list-a", "" } },
      _ => new { type = "imageVisible", imageIds = new[] { "list-a", "no-such-image" } }
    };

    var (status, body) = await PostAsync(client, "/api/sequences", SequencePayload(condition)).ConfigureAwait(false);

    status.Should().Be(HttpStatusCode.BadRequest, body.ToString());
    if (kind == "missing") body.ToString().Should().Contain("Image reference 'no-such-image' does not exist");
  }

  // ---------- one-value positions ----------

  [Fact]
  public async Task ListParameterInAOneValuePositionIsRefusedWithTheNameAndThePath() {
    using var app = CreateFactory();
    var client = await ClientAsync(app).ConfigureAwait(false);

    var (status, body) = await PostAsync(client, "/api/commands", new {
      name = $"tap-list-{Guid.NewGuid():N}",
      parameters = QuestsDeclaration,
      steps = new object[] {
        new { type = "PrimitiveTap", order = 0, primitiveTap = new { detectionTarget = new { referenceImageId = "{{quests}}" } } }
      }
    }).ConfigureAwait(false);

    status.Should().Be(HttpStatusCode.BadRequest, body.ToString());
    var text = body.ToString();
    text.Should().Contain("list_in_single_field").And.Contain("quests").And.Contain("primitiveTap.detectionTarget.referenceImageId");
  }

  // ---------- WaitForImage ----------

  [Fact]
  public async Task WaitForImageListPlaceholderAndWrittenListAreAccepted() {
    using var app = CreateFactory();
    var client = await ClientAsync(app).ConfigureAwait(false);

    var (placeholderStatus, placeholderBody) = await PostAsync(client, "/api/commands", new {
      name = $"wait-list-{Guid.NewGuid():N}",
      parameters = QuestsDeclaration,
      steps = new object[] {
        new { type = "WaitForImage", order = 0, waitForImage = new { detectionTarget = new { referenceImageId = "{{quests}}" }, timeoutMs = 3000 } }
      }
    }).ConfigureAwait(false);
    placeholderStatus.Should().Be(HttpStatusCode.Created, placeholderBody.ToString());
    placeholderBody.GetProperty("warnings").EnumerateArray()
      .Should().Contain(w => w.GetProperty("code").GetString() == "static_check_skipped");

    var (status, body) = await PostAsync(client, "/api/commands", new {
      name = $"wait-written-{Guid.NewGuid():N}",
      steps = new object[] {
        new { type = "WaitForImage", order = 0, waitForImage = new { detectionTarget = new { referenceImageIds = new[] { "list-a", "list-b" } }, timeoutMs = 3000 } }
      }
    }).ConfigureAwait(false);
    status.Should().Be(HttpStatusCode.Created, body.ToString());
    var id = body.GetProperty("id").GetString();
    var get = await client.GetAsync(new Uri($"/api/commands/{id}", UriKind.Relative)).ConfigureAwait(false);
    var fetched = JsonDocument.Parse(await get.Content.ReadAsStringAsync().ConfigureAwait(false)).RootElement;
    var target = fetched.GetProperty("steps")[0].GetProperty("waitForImage").GetProperty("detectionTarget");
    target.GetProperty("referenceImageIds").EnumerateArray().Select(e => e.GetString()).Should().Equal("list-a", "list-b");
    target.TryGetProperty("referenceImageId", out _).Should().BeFalse();
  }

  [Fact]
  public async Task WaitForImageWithBothFieldsAndATapWithAListAreRefused() {
    using var app = CreateFactory();
    var client = await ClientAsync(app).ConfigureAwait(false);

    var (bothStatus, bothBody) = await PostAsync(client, "/api/commands", new {
      name = $"wait-both-{Guid.NewGuid():N}",
      steps = new object[] {
        new { type = "WaitForImage", order = 0, waitForImage = new { detectionTarget = new { referenceImageId = "list-a", referenceImageIds = new[] { "list-b" } } } }
      }
    }).ConfigureAwait(false);
    bothStatus.Should().Be(HttpStatusCode.BadRequest, bothBody.ToString());

    var (tapStatus, tapBody) = await PostAsync(client, "/api/commands", new {
      name = $"tap-ids-{Guid.NewGuid():N}",
      steps = new object[] {
        new { type = "PrimitiveTap", order = 0, primitiveTap = new { detectionTarget = new { referenceImageId = "list-a", referenceImageIds = new[] { "list-b" } } } }
      }
    }).ConfigureAwait(false);
    tapStatus.Should().Be(HttpStatusCode.BadRequest, tapBody.ToString());
  }

  [Fact]
  public async Task OldWaitForImageStepIsUnchanged() {
    using var app = CreateFactory();
    var client = await ClientAsync(app).ConfigureAwait(false);

    var (status, body) = await PostAsync(client, "/api/commands", new {
      name = $"wait-old-{Guid.NewGuid():N}",
      steps = new object[] {
        new { type = "WaitForImage", order = 0, waitForImage = new { detectionTarget = new { referenceImageId = "list-a" }, timeoutMs = 3000 } }
      }
    }).ConfigureAwait(false);

    status.Should().Be(HttpStatusCode.Created, body.ToString());
    var target = body.GetProperty("steps")[0].GetProperty("waitForImage").GetProperty("detectionTarget");
    target.GetProperty("referenceImageId").GetString().Should().Be("list-a");
    target.TryGetProperty("referenceImageIds", out _).Should().BeFalse();
  }
}
