using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Linq;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace GameBot.ContractTests.Sequences;

/// <summary>
/// The API contract for composite conditions (feature 088, issue #191).
/// <para>
/// The central assertion here is <b>400, never 500</b>. A malformed condition that escapes the save
/// path's validation reaches the repository, which throws, and the author gets an opaque server
/// error instead of a message naming the offending child. Each malformed shape therefore gets its
/// own case rather than being covered by one representative example.
/// </para>
/// </summary>
public sealed class CompositeConditionContractTests {
  private static WebApplicationFactory<Program> CreateFactory() {
    Environment.SetEnvironmentVariable("GAMEBOT_USE_ADB", "false");
    Environment.SetEnvironmentVariable("GAMEBOT_DYNAMIC_PORT", "true");
    Environment.SetEnvironmentVariable("GAMEBOT_AUTH_TOKEN", "test-token");
    return new WebApplicationFactory<Program>();
  }

  private const string OneByOnePngBase64 =
    "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO2n5u4AAAAASUVORK5CYII=";

  /// <summary>
  /// Builds an authenticated client with every reference image these tests name already registered,
  /// so an assertion about composite validation is never confounded by a dangling-image error.
  /// </summary>
  private static async Task<System.Net.Http.HttpClient> ClientAsync(WebApplicationFactory<Program> app) {
    var client = app.CreateClient();
    client.DefaultRequestHeaders.Add("Authorization", "Bearer test-token");

    var ids = new List<string> { "a", "b", "pns-disconnect-confirm", "pns-gas-dialog-title", "pns-city-hud" };
    ids.AddRange(WideImageIds());
    await RegisterImagesAsync(client, ids.ToArray()).ConfigureAwait(false);

    return client;
  }

  /// <summary>
  /// Registers the reference images a payload names, because the save path rejects a dangling image
  /// reference — and, as of this feature, does so for images named inside a composite too. Tests that
  /// expect a 201 must therefore provide real images, which is itself a check that the new recursive
  /// reference walk resolves rather than merely rejects.
  /// </summary>
  private static async Task RegisterImagesAsync(System.Net.Http.HttpClient client, params string[] imageIds) {
    foreach (var imageId in imageIds) {
      var response = await client.PostAsJsonAsync(
        new Uri("/api/images", UriKind.Relative),
        new { id = imageId, data = OneByOnePngBase64 }).ConfigureAwait(false);

      // Created on first use; a later test in the same data directory may already have it.
      response.StatusCode.Should().BeOneOf(HttpStatusCode.Created, HttpStatusCode.OK, HttpStatusCode.Conflict);
    }
  }

  private static string[] WideImageIds() => Enumerable.Range(0, 17).Select(i => $"img-{i}").ToArray();

  private static object Tap(string stepId, object? condition = null) => condition is null
    ? new {
      stepId,
      primitiveAction = new { type = "tap", schemaVersion = "v1", payload = new { x = 50, y = 50 } }
    }
    : (object)new {
      stepId,
      primitiveAction = new { type = "tap", schemaVersion = "v1", payload = new { x = 50, y = 50 } },
      condition
    };

  private static object SequencePayload(string name, object condition) => new {
    name,
    version = 1,
    steps = new[] { Tap("guarded", condition) }
  };

  private static object Image(string imageId) => new { type = "imageVisible", imageId };

  // ---------- rejections: every one must be a 400 ----------

  public static TheoryData<string, object, string> MalformedConditions() {
    var deep = BuildNested(5);

    return new TheoryData<string, object, string> {
      { "empty-children", new { type = "all", children = Array.Empty<object>() }, "at least one child" },
      { "absent-children", new { type = "any" }, "at least one child" },
      {
        "too-many-children",
        new { type = "all", children = Enumerable.Range(0, 17).Select(i => Image($"img-{i}")).ToArray() },
        "at most 16 children"
      },
      { "too-deep", deep, "maximum depth of 4" },
      {
        "child-missing-image-id",
        new { type = "all", children = new object[] { new { type = "imageVisible", imageId = "" } } },
        "requires imageId"
      },
      {
        "child-similarity-out-of-range",
        new { type = "all", children = new object[] { new { type = "imageVisible", imageId = "a", minSimilarity = 1.5 } } },
        "within 0..1"
      },
      {
        "child-missing-step-ref",
        new { type = "all", children = new object[] { new { type = "commandOutcome", stepRef = "", expectedState = "success" } } },
        "requires stepRef"
      },
      {
        "child-unknown-expected-state",
        new { type = "all", children = new object[] { new { type = "commandOutcome", stepRef = "guarded", expectedState = "maybe" } } },
        "expectedState"
      },
      {
        // Feature 103 (issue #193, FR-008): before this feature a reference reached through a
        // composite was checked for non-emptiness only, so this saved with 201 and then failed the
        // run with "reference is not available" — the opposite of the 400-not-500 posture the rest
        // of this file exists to enforce.
        "child-dangling-step-ref",
        new { type = "all", children = new object[] { new { type = "commandOutcome", stepRef = "no-such-step", expectedState = "success" } } },
        "references unknown prior step"
      }
    };
  }

  private static object BuildNested(int levels) {
    object current = Image("a");
    for (var level = 1; level < levels; level++) {
      current = new { type = "all", children = new[] { current } };
    }

    return current;
  }

  [Theory]
  [MemberData(nameof(MalformedConditions))]
  public async Task CreateRejectsAMalformedCompositeWithFourHundredNotFiveHundred(string caseName, object condition, string expectedFragment) {
    using var app = CreateFactory();
    var client = await ClientAsync(app).ConfigureAwait(false);

    var response = await client.PostAsJsonAsync("/api/sequences", SequencePayload($"bad-{caseName}-{Guid.NewGuid():N}", condition)).ConfigureAwait(false);
    var content = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

    response.StatusCode.Should().Be(HttpStatusCode.BadRequest, "case '{0}' returned {1}: {2}", caseName, response.StatusCode, content);
    content.Should().Contain(expectedFragment);
  }

  [Theory]
  [MemberData(nameof(MalformedConditions))]
  public async Task UpdateRejectsAMalformedCompositeWithFourHundredNotFiveHundred(string caseName, object condition, string expectedFragment) {
    using var app = CreateFactory();
    var client = await ClientAsync(app).ConfigureAwait(false);

    var created = await client.PostAsJsonAsync(
      "/api/sequences",
      SequencePayload($"good-{caseName}-{Guid.NewGuid():N}", new { type = "all", children = new[] { Image("a") } })).ConfigureAwait(false);
    created.StatusCode.Should().Be(HttpStatusCode.Created);

    var id = JsonDocument.Parse(await created.Content.ReadAsStringAsync().ConfigureAwait(false))
      .RootElement.GetProperty("id").GetString();

    var response = await client.PutAsJsonAsync(
      $"/api/sequences/{id}",
      SequencePayload($"updated-{caseName}", condition)).ConfigureAwait(false);
    var content = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

    response.StatusCode.Should().Be(HttpStatusCode.BadRequest, "case '{0}' returned {1}: {2}", caseName, response.StatusCode, content);
    content.Should().Contain(expectedFragment);
  }

  [Fact]
  public async Task AnUnknownConditionDiscriminatorIsRejectedAsABadRequest() {
    using var app = CreateFactory();
    var client = await ClientAsync(app).ConfigureAwait(false);

    var response = await client.PostAsJsonAsync(
      "/api/sequences",
      SequencePayload($"unknown-rule-{Guid.NewGuid():N}", new { type = "whenever", children = new[] { Image("a") } })).ConfigureAwait(false);

    response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
  }

  // ---------- acceptance and round-trip ----------

  [Fact]
  public async Task AValidCompositeIsAcceptedAndReturnedWithItsChildren() {
    using var app = CreateFactory();
    var client = await ClientAsync(app).ConfigureAwait(false);

    var condition = new {
      type = "all",
      children = new object[] {
        new { type = "imageVisible", imageId = "pns-disconnect-confirm", minSimilarity = 0.85 },
        new { type = "none", children = new object[] { Image("pns-gas-dialog-title") } }
      }
    };

    var created = await client.PostAsJsonAsync("/api/sequences", SequencePayload($"b011-{Guid.NewGuid():N}", condition)).ConfigureAwait(false);
    var body = await created.Content.ReadAsStringAsync().ConfigureAwait(false);

    created.StatusCode.Should().Be(HttpStatusCode.Created, body);

    var root = JsonDocument.Parse(body).RootElement;
    var guard = root.GetProperty("steps")[0].GetProperty("condition");

    guard.GetProperty("type").GetString().Should().Be("all");
    var children = guard.GetProperty("children");
    children.GetArrayLength().Should().Be(2);
    children[0].GetProperty("imageId").GetString().Should().Be("pns-disconnect-confirm");
    children[1].GetProperty("type").GetString().Should().Be("none");
    children[1].GetProperty("children")[0].GetProperty("imageId").GetString().Should().Be("pns-gas-dialog-title");
  }

  [Theory]
  [InlineData("all")]
  [InlineData("any")]
  [InlineData("none")]
  public async Task EveryRuleIsAcceptedAndRoundTrips(string rule) {
    using var app = CreateFactory();
    var client = await ClientAsync(app).ConfigureAwait(false);

    var created = await client.PostAsJsonAsync(
      "/api/sequences",
      SequencePayload($"rule-{rule}-{Guid.NewGuid():N}", new { type = rule, children = new[] { Image("a") } })).ConfigureAwait(false);
    var body = await created.Content.ReadAsStringAsync().ConfigureAwait(false);

    created.StatusCode.Should().Be(HttpStatusCode.Created, body);
    JsonDocument.Parse(body).RootElement
      .GetProperty("steps")[0].GetProperty("condition").GetProperty("type").GetString()
      .Should().Be(rule);
  }

  [Fact]
  public async Task ASingleChildCompositeIsAccepted() {
    using var app = CreateFactory();
    var client = await ClientAsync(app).ConfigureAwait(false);

    var response = await client.PostAsJsonAsync(
      "/api/sequences",
      SequencePayload($"single-child-{Guid.NewGuid():N}", new { type = "all", children = new[] { Image("a") } })).ConfigureAwait(false);

    response.StatusCode.Should().Be(HttpStatusCode.Created);
  }

  [Fact]
  public async Task ExactlySixteenChildrenAndFourLevelsAreAccepted() {
    using var app = CreateFactory();
    var client = await ClientAsync(app).ConfigureAwait(false);

    var wide = new { type = "all", children = Enumerable.Range(0, 16).Select(i => Image($"img-{i}")).ToArray() };
    var wideResponse = await client.PostAsJsonAsync("/api/sequences", SequencePayload($"wide-{Guid.NewGuid():N}", wide)).ConfigureAwait(false);
    wideResponse.StatusCode.Should().Be(HttpStatusCode.Created, await wideResponse.Content.ReadAsStringAsync().ConfigureAwait(false));

    var deepResponse = await client.PostAsJsonAsync("/api/sequences", SequencePayload($"deep-{Guid.NewGuid():N}", BuildNested(4))).ConfigureAwait(false);
    deepResponse.StatusCode.Should().Be(HttpStatusCode.Created, await deepResponse.Content.ReadAsStringAsync().ConfigureAwait(false));
  }

  [Fact]
  public async Task ExistingSingleConditionSequencesAreStillAccepted() {
    // FR-015: the change is additive; a payload written before composites existed must be unaffected.
    using var app = CreateFactory();
    var client = await ClientAsync(app).ConfigureAwait(false);

    var response = await client.PostAsJsonAsync(
      "/api/sequences",
      SequencePayload($"legacy-{Guid.NewGuid():N}", new { type = "imageVisible", imageId = "a", minSimilarity = 0.8 })).ConfigureAwait(false);
    var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

    response.StatusCode.Should().Be(HttpStatusCode.Created, body);

    var guard = JsonDocument.Parse(body).RootElement.GetProperty("steps")[0].GetProperty("condition");
    guard.GetProperty("type").GetString().Should().Be("imageVisible");
    guard.TryGetProperty("children", out _).Should().BeFalse("a leaf must not grow a children array");
  }

  // ---------- feature 103 (issue #193): reference scope at the API boundary ----------

  [Fact]
  public async Task ACompositeNestedReferenceToALaterStepIsRejectedAsNotPrior() {
    // Reachable but authored after the condition asking about it. Resolution and ordering are two
    // separate rules, and a fix that only resolved would leave this accepted.
    using var app = CreateFactory();
    var client = await ClientAsync(app).ConfigureAwait(false);

    var payload = new {
      name = $"forward-ref-{Guid.NewGuid():N}",
      version = 1,
      steps = new object[] {
        Tap("gate", new { type = "all", children = new object[] { new { type = "commandOutcome", stepRef = "later", expectedState = "success" } } }),
        Tap("later")
      }
    };

    var response = await client.PostAsJsonAsync("/api/sequences", payload).ConfigureAwait(false);
    var content = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

    response.StatusCode.Should().Be(HttpStatusCode.BadRequest, content);
    content.Should().Contain("must reference a prior step");
  }

  [Fact]
  public async Task AReferenceWrittenDirectlyInAnIfConditionIsRejectedWhenItNamesAnAbsentStep() {
    // FR-008a. Measurement found this slot never resolved a reference at all, so feature 081's own
    // acceptance scenario for an If condition passed vacuously.
    using var app = CreateFactory();
    var client = await ClientAsync(app).ConfigureAwait(false);

    var payload = new {
      name = $"if-dangling-{Guid.NewGuid():N}",
      version = 1,
      steps = new object[] {
        Tap("first"),
        new {
          stepId = "branch",
          stepType = "If",
          @if = new { condition = new { type = "commandOutcome", stepRef = "no-such-step", expectedState = "success" } },
          body = new object[] { Tap("then-step") }
        }
      }
    };

    var response = await client.PostAsJsonAsync("/api/sequences", payload).ConfigureAwait(false);
    var content = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

    response.StatusCode.Should().Be(HttpStatusCode.BadRequest, content);
    content.Should().Contain("references unknown prior step");
  }

  [Theory]
  [InlineData("all")]
  [InlineData("any")]
  [InlineData("none")]
  public async Task ACompositeNestedReferenceToAStepInsideAnEarlierLoopBodyIsAccepted(string rule) {
    // The over-reach guard, and the headline of issue #193 at the HTTP level: the reference reaches
    // into a loop body, expectedState is "break", and it is wrapped in a composite. All three of
    // those were once reasons to reject; none is now.
    using var app = CreateFactory();
    var client = await ClientAsync(app).ConfigureAwait(false);

    var payload = new {
      name = $"nested-ok-{rule}-{Guid.NewGuid():N}",
      version = 1,
      steps = new object[] {
        new {
          stepId = "loop1",
          stepType = "Loop",
          loop = new { loopType = "count", count = 3 },
          body = new object[] {
            Tap("probe"),
            new { stepId = "nested-break", stepType = "Break", breakCondition = Image("pns-city-hud") }
          }
        },
        Tap("gate", new {
          type = rule,
          children = new object[] {
            Image("a"),
            new { type = "commandOutcome", stepRef = "nested-break", expectedState = "break" }
          }
        })
      }
    };

    var response = await client.PostAsJsonAsync("/api/sequences", payload).ConfigureAwait(false);

    response.StatusCode.Should().Be(
      HttpStatusCode.Created, await response.Content.ReadAsStringAsync().ConfigureAwait(false));
  }

  [Fact]
  public async Task ACompositeIsAcceptedInABreakConditionAndInAnIfCondition() {
    using var app = CreateFactory();
    var client = await ClientAsync(app).ConfigureAwait(false);

    var payload = new {
      name = $"positions-{Guid.NewGuid():N}",
      version = 1,
      steps = new object[] {
        new {
          stepId = "drain",
          stepType = "Loop",
          loop = new { loopType = "count", count = 3 },
          body = new object[] {
            new {
              stepId = "brk",
              stepType = "Break",
              breakCondition = new { type = "any", children = new[] { Image("pns-city-hud") } }
            }
          }
        },
        new {
          stepId = "branch",
          stepType = "If",
          @if = new { condition = new { type = "all", children = new[] { Image("a"), Image("b") } } },
          body = new object[] { Tap("then-step") }
        }
      }
    };

    var response = await client.PostAsJsonAsync("/api/sequences", payload).ConfigureAwait(false);

    response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync().ConfigureAwait(false));
  }
}
