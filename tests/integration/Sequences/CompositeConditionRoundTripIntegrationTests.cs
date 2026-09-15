using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace GameBot.IntegrationTests.Sequences;

/// <summary>
/// Composite conditions must survive the full create → read → update → read cycle through real
/// persistence (feature 088, FR-013), and sequences written before composites existed must be
/// completely unaffected by the change (FR-015).
/// </summary>
public sealed class CompositeConditionRoundTripIntegrationTests {
  private const string OneByOnePngBase64 =
    "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO2n5u4AAAAASUVORK5CYII=";

  private static async Task<HttpClient> StartAsync(WebApplicationFactory<Program> app, params string[] imageIds) {
    var client = app.CreateClient();
    client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "test-token");

    foreach (var imageId in imageIds) {
      var upload = await client.PostAsJsonAsync(
        new Uri("/api/images", UriKind.Relative),
        new { id = imageId, data = OneByOnePngBase64 }).ConfigureAwait(false);
      upload.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    return client;
  }

  private static object GuardedStep(object condition) => new {
    stepId = "guarded",
    label = "Guarded",
    primitiveAction = new { type = "tap", schemaVersion = "v1", payload = new { x = 120, y = 840 } },
    condition
  };

  /// <summary>
  /// A depth-3 composite mixing both leaf kinds, negation on a leaf and on a nested composite — so a
  /// round trip that silently dropped any one of those would fail here rather than in production.
  /// </summary>
  private static object NestedComposite() => new {
    type = "all",
    children = new object[] {
      new { type = "imageVisible", imageId = "confirm", minSimilarity = 0.85 },
      new {
        type = "none",
        negate = false,
        children = new object[] {
          new { type = "imageVisible", imageId = "gas-title", minSimilarity = 0.7, negate = true },
          new { type = "any", children = new object[] { new { type = "imageVisible", imageId = "banner" } } }
        }
      },
      new { type = "imageVisible", imageId = "banner", negate = true }
    }
  };

  [Fact]
  public async Task ANestedCompositeSurvivesCreateReadUpdateAndReadAgain() {
    TestEnvironment.PrepareCleanDataDir();
    Environment.SetEnvironmentVariable("GAMEBOT_AUTH_TOKEN", "test-token");

    using var app = new WebApplicationFactory<Program>();
    var client = await StartAsync(app, "confirm", "gas-title", "banner").ConfigureAwait(false);

    var payload = new {
      name = "composite-round-trip",
      version = 1,
      steps = new object[] { GuardedStep(NestedComposite()) }
    };

    var createResponse = await client.PostAsJsonAsync("/api/sequences", payload).ConfigureAwait(false);
    createResponse.StatusCode.Should().Be(
      HttpStatusCode.Created,
      await createResponse.Content.ReadAsStringAsync().ConfigureAwait(false));

    var sequenceId = (await createResponse.Content.ReadFromJsonAsync<JsonElement>().ConfigureAwait(false))
      .GetProperty("id").GetString();

    var afterCreate = await ReadGuardAsync(client, sequenceId!).ConfigureAwait(false);
    AssertNestedCompositeShape(afterCreate);

    // Update with the same condition: a re-save must not degrade what it just read back.
    var updateResponse = await client.PutAsJsonAsync($"/api/sequences/{sequenceId}", payload).ConfigureAwait(false);
    updateResponse.StatusCode.Should().Be(
      HttpStatusCode.OK,
      await updateResponse.Content.ReadAsStringAsync().ConfigureAwait(false));

    var afterUpdate = await ReadGuardAsync(client, sequenceId!).ConfigureAwait(false);
    AssertNestedCompositeShape(afterUpdate);

    afterUpdate.GetRawText().Should().Be(afterCreate.GetRawText(), "a no-op re-save must not change the stored condition");
  }

  private static async Task<JsonElement> ReadGuardAsync(HttpClient client, string sequenceId) {
    var response = await client.GetAsync(new Uri($"/api/sequences/{sequenceId}", UriKind.Relative)).ConfigureAwait(false);
    response.StatusCode.Should().Be(HttpStatusCode.OK);

    var document = await response.Content.ReadFromJsonAsync<JsonElement>().ConfigureAwait(false);
    return document.GetProperty("steps")[0].GetProperty("condition").Clone();
  }

  private static void AssertNestedCompositeShape(JsonElement guard) {
    guard.GetProperty("type").GetString().Should().Be("all");

    var children = guard.GetProperty("children");
    children.GetArrayLength().Should().Be(3, "child count and order are the author's and must not change");

    children[0].GetProperty("imageId").GetString().Should().Be("confirm");
    children[0].GetProperty("minSimilarity").GetDouble().Should().Be(0.85);

    var none = children[1];
    none.GetProperty("type").GetString().Should().Be("none");
    var nested = none.GetProperty("children");
    nested.GetArrayLength().Should().Be(2);
    nested[0].GetProperty("imageId").GetString().Should().Be("gas-title");
    nested[0].GetProperty("minSimilarity").GetDouble().Should().Be(0.7);
    nested[0].GetProperty("negate").GetBoolean().Should().BeTrue();
    nested[1].GetProperty("type").GetString().Should().Be("any");
    nested[1].GetProperty("children")[0].GetProperty("imageId").GetString().Should().Be("banner");

    children[2].GetProperty("negate").GetBoolean().Should().BeTrue();
  }

  [Fact]
  public async Task ASingleConditionSequenceRoundTripsExactlyAsItDidBefore() {
    // FR-015. The two leaf forms were untouched by this feature, and this is the assertion that says
    // so from the outside: a leaf must not acquire a children array or lose any of its own fields.
    TestEnvironment.PrepareCleanDataDir();
    Environment.SetEnvironmentVariable("GAMEBOT_AUTH_TOKEN", "test-token");

    using var app = new WebApplicationFactory<Program>();
    var client = await StartAsync(app, "confirm").ConfigureAwait(false);

    var payload = new {
      name = "legacy-round-trip",
      version = 1,
      steps = new object[] {
        new {
          stepId = "first",
          primitiveAction = new { type = "tap", schemaVersion = "v1", payload = new { x = 10, y = 10 } }
        },
        GuardedStep(new { type = "imageVisible", imageId = "confirm", minSimilarity = 0.8 }),
        new {
          stepId = "after",
          primitiveAction = new { type = "tap", schemaVersion = "v1", payload = new { x = 20, y = 20 } },
          condition = new { type = "commandOutcome", stepRef = "first", expectedState = "success" }
        }
      }
    };

    var createResponse = await client.PostAsJsonAsync("/api/sequences", payload).ConfigureAwait(false);
    createResponse.StatusCode.Should().Be(
      HttpStatusCode.Created,
      await createResponse.Content.ReadAsStringAsync().ConfigureAwait(false));

    var sequenceId = (await createResponse.Content.ReadFromJsonAsync<JsonElement>().ConfigureAwait(false))
      .GetProperty("id").GetString();

    var beforeUpdate = await ReadAllStepsAsync(client, sequenceId!).ConfigureAwait(false);

    var imageGuard = beforeUpdate[1].GetProperty("condition");
    imageGuard.GetProperty("type").GetString().Should().Be("imageVisible");
    imageGuard.GetProperty("imageId").GetString().Should().Be("confirm");
    imageGuard.GetProperty("minSimilarity").GetDouble().Should().Be(0.8);
    imageGuard.TryGetProperty("children", out _).Should().BeFalse("a leaf must not grow a children array");

    var outcomeGuard = beforeUpdate[2].GetProperty("condition");
    outcomeGuard.GetProperty("type").GetString().Should().Be("commandOutcome");
    outcomeGuard.GetProperty("stepRef").GetString().Should().Be("first");
    outcomeGuard.GetProperty("expectedState").GetString().Should().Be("success");
    outcomeGuard.TryGetProperty("children", out _).Should().BeFalse();

    var updateResponse = await client.PutAsJsonAsync($"/api/sequences/{sequenceId}", payload).ConfigureAwait(false);
    updateResponse.StatusCode.Should().Be(HttpStatusCode.OK);

    var afterUpdate = await ReadAllStepsAsync(client, sequenceId!).ConfigureAwait(false);
    afterUpdate[1].GetProperty("condition").GetRawText().Should().Be(imageGuard.GetRawText());
    afterUpdate[2].GetProperty("condition").GetRawText().Should().Be(outcomeGuard.GetRawText());
  }

  private static async Task<JsonElement[]> ReadAllStepsAsync(HttpClient client, string sequenceId) {
    var response = await client.GetAsync(new Uri($"/api/sequences/{sequenceId}", UriKind.Relative)).ConfigureAwait(false);
    response.StatusCode.Should().Be(HttpStatusCode.OK);

    var document = await response.Content.ReadFromJsonAsync<JsonElement>().ConfigureAwait(false);
    var steps = document.GetProperty("steps");
    var result = new JsonElement[steps.GetArrayLength()];
    for (var index = 0; index < result.Length; index++) {
      result[index] = steps[index].Clone();
    }

    return result;
  }

  [Fact]
  public async Task ACompositeSurvivesInEveryConditionPosition() {
    // FR-005: step guard, break condition, if condition, while condition and repeat-until condition.
    TestEnvironment.PrepareCleanDataDir();
    Environment.SetEnvironmentVariable("GAMEBOT_AUTH_TOKEN", "test-token");

    using var app = new WebApplicationFactory<Program>();
    var client = await StartAsync(app, "confirm", "gas-title").ConfigureAwait(false);

    object composite = new {
      type = "all",
      children = new object[] {
        new { type = "imageVisible", imageId = "confirm" },
        new { type = "imageVisible", imageId = "gas-title", negate = true }
      }
    };

    var payload = new {
      name = "composite-positions",
      version = 1,
      steps = new object[] {
        GuardedStep(composite),
        new {
          stepId = "while-loop",
          stepType = "Loop",
          loop = new { loopType = "while", maxIterations = 3, exitOnMaxIterations = true, condition = composite },
          body = new object[] {
            new {
              stepId = "brk",
              stepType = "Break",
              breakCondition = composite
            }
          }
        },
        new {
          stepId = "repeat-loop",
          stepType = "Loop",
          loop = new { loopType = "repeatUntil", maxIterations = 3, exitOnMaxIterations = true, condition = composite },
          body = new object[] {
            new {
              stepId = "repeat-body",
              primitiveAction = new { type = "tap", schemaVersion = "v1", payload = new { x = 5, y = 5 } }
            }
          }
        },
        new {
          stepId = "branch",
          stepType = "If",
          @if = new { condition = composite },
          body = new object[] {
            new {
              stepId = "then-step",
              primitiveAction = new { type = "tap", schemaVersion = "v1", payload = new { x = 7, y = 7 } }
            }
          }
        }
      }
    };

    var createResponse = await client.PostAsJsonAsync("/api/sequences", payload).ConfigureAwait(false);
    createResponse.StatusCode.Should().Be(
      HttpStatusCode.Created,
      await createResponse.Content.ReadAsStringAsync().ConfigureAwait(false));

    var sequenceId = (await createResponse.Content.ReadFromJsonAsync<JsonElement>().ConfigureAwait(false))
      .GetProperty("id").GetString();

    var steps = await ReadAllStepsAsync(client, sequenceId!).ConfigureAwait(false);

    steps[0].GetProperty("condition").GetProperty("type").GetString().Should().Be("all");
    steps[1].GetProperty("loop").GetProperty("condition").GetProperty("type").GetString().Should().Be("all");
    steps[1].GetProperty("body")[0].GetProperty("breakCondition").GetProperty("type").GetString().Should().Be("all");
    steps[2].GetProperty("loop").GetProperty("condition").GetProperty("type").GetString().Should().Be("all");
    steps[3].GetProperty("if").GetProperty("condition").GetProperty("type").GetString().Should().Be("all");
  }
}
