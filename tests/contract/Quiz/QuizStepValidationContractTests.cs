using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using GameBot.Domain.Quiz;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

#pragma warning disable CA2007, CA1861

namespace GameBot.ContractTests.Quiz;

/// <summary>
/// Feature 142: a quiz step with a wrong shape is an actionable 400, never a 500. A 500 means the action type reached the
/// repository without being registered in one of the six places (see the sync test). A valid pair is saved.
/// </summary>
public sealed class QuizStepValidationContractTests {
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

  internal static object Region(int y) => new { x = 40, y, width = 400, height = 60 };

  internal static Dictionary<string, object?> LookupPayload(string game = "pns") => new() {
    ["game"] = game,
    ["category"] = "radio",
    ["store"] = "pns-quiz",
    ["question"] = Region(200),
    ["answers"] = new object[] { Region(320), Region(400), Region(480) },
    ["similarity"] = 0.85
  };

  internal static object Step(string id, string type, object payload) => new {
    stepId = id,
    label = id,
    stepType = "Action",
    primitiveAction = new { type, schemaVersion = "1", payload }
  };

  private static async Task<(HttpStatusCode Status, string Body)> SaveAsync(System.Net.Http.HttpClient client, params object[] steps) {
    var response = await client.PostAsJsonAsync("/api/sequences", new {
      name = "quiz-contract-" + Guid.NewGuid().ToString("N"),
      version = 1,
      steps
    }).ConfigureAwait(false);
    return (response.StatusCode, await response.Content.ReadAsStringAsync().ConfigureAwait(false));
  }

  [Fact]
  public async Task AValidLookupAndLearnPairIsSaved() {
    using var app = CreateFactory();
    var client = AuthedClient(app);

    var (status, _) = await SaveAsync(client,
      Step("lookup-1", "quiz-lookup", LookupPayload()),
      Step("learn-1", "quiz-learn", new { lookupStepId = "lookup-1", correctPosition = 2 }));

    status.Should().Be(HttpStatusCode.Created);
  }

  [Fact]
  public async Task AValidLookupRoundTripsItsRegions() {
    using var app = CreateFactory();
    var client = AuthedClient(app);
    var create = await client.PostAsJsonAsync("/api/sequences", new {
      name = "quiz-roundtrip-" + Guid.NewGuid().ToString("N"),
      version = 1,
      steps = new[] { Step("lookup-1", "quiz-lookup", LookupPayload()) }
    }).ConfigureAwait(false);
    create.StatusCode.Should().Be(HttpStatusCode.Created);
    var id = (await create.Content.ReadFromJsonAsync<JsonElement>().ConfigureAwait(false)).GetProperty("id").GetString();

    var get = await client.GetAsync(new Uri($"/api/sequences/{id}", UriKind.Relative)).ConfigureAwait(false);
    var payload = (await get.Content.ReadFromJsonAsync<JsonElement>().ConfigureAwait(false))
      .GetProperty("steps")[0].GetProperty("primitiveAction").GetProperty("payload");

    payload.GetProperty("game").GetString().Should().Be("pns");
    payload.GetProperty("answers").GetArrayLength().Should().Be(3);
  }

  [Theory]
  [InlineData("game")]
  [InlineData("category")]
  [InlineData("store")]
  public async Task ALookupWithoutAGameCategoryOrStoreIsRejectedWith400(string missing) {
    using var app = CreateFactory();
    var client = AuthedClient(app);
    var payload = LookupPayload(game: "no-record-" + Guid.NewGuid().ToString("N"));
    payload.Remove(missing);

    var (status, body) = await SaveAsync(client, Step("lookup-1", "quiz-lookup", payload));

    status.Should().Be(HttpStatusCode.BadRequest, "a wrong quiz step is a save-time rejection, not a server fault");
    body.Should().Contain(missing);
  }

  [Theory]
  [InlineData("game")]
  [InlineData("category")]
  [InlineData("store")]
  public async Task ALookupWithABlankGameCategoryOrStoreIsRejectedWith400(string blank) {
    using var app = CreateFactory();
    var client = AuthedClient(app);
    var payload = LookupPayload();
    payload[blank] = "   ";

    var (status, body) = await SaveAsync(client, Step("lookup-1", "quiz-lookup", payload));

    status.Should().Be(HttpStatusCode.BadRequest);
    body.Should().Contain(blank);
  }

  [Fact]
  public async Task ALookupWithOneAnswerIsRejectedWith400() {
    using var app = CreateFactory();
    var client = AuthedClient(app);
    var payload = LookupPayload();
    payload["answers"] = new object[] { Region(320) };

    var (status, body) = await SaveAsync(client, Step("lookup-1", "quiz-lookup", payload));

    status.Should().Be(HttpStatusCode.BadRequest);
    body.Should().Contain("answers");
  }

  [Fact]
  public async Task ALookupWithARegionOfNoSizeIsRejectedWith400() {
    using var app = CreateFactory();
    var client = AuthedClient(app);
    var payload = LookupPayload();
    payload["question"] = new { x = 1, y = 1, width = 0, height = 10 };

    var (status, body) = await SaveAsync(client, Step("lookup-1", "quiz-lookup", payload));

    status.Should().Be(HttpStatusCode.BadRequest);
    body.Should().Contain("question");
  }

  [Theory]
  [InlineData(1.5)]
  [InlineData(-0.2)]
  public async Task ALookupWithASimilarityOutsideZeroToOneIsRejectedWith400(double similarity) {
    using var app = CreateFactory();
    var client = AuthedClient(app);
    var payload = LookupPayload();
    payload["similarity"] = similarity;

    var (status, body) = await SaveAsync(client, Step("lookup-1", "quiz-lookup", payload));

    status.Should().Be(HttpStatusCode.BadRequest);
    body.Should().Contain("similarity");
  }

  [Fact]
  public async Task ALearnWithoutALookupStepIdIsRejectedWith400() {
    using var app = CreateFactory();
    var client = AuthedClient(app);

    var (status, body) = await SaveAsync(client,
      Step("lookup-1", "quiz-lookup", LookupPayload()),
      Step("learn-1", "quiz-learn", new { correctPosition = 2 }));

    status.Should().Be(HttpStatusCode.BadRequest);
    body.Should().Contain("lookupStepId");
  }

  [Fact]
  public async Task ALearnThatNamesNoEarlierLookupIsRejectedWith400() {
    using var app = CreateFactory();
    var client = AuthedClient(app);

    var (unknown, unknownBody) = await SaveAsync(client,
      Step("learn-1", "quiz-learn", new { lookupStepId = "missing", correctPosition = 1 }));
    var (late, lateBody) = await SaveAsync(client,
      Step("learn-1", "quiz-learn", new { lookupStepId = "lookup-1", correctPosition = 1 }),
      Step("lookup-1", "quiz-lookup", LookupPayload()));

    unknown.Should().Be(HttpStatusCode.BadRequest);
    unknownBody.Should().Contain("quiz-lookup");
    late.Should().Be(HttpStatusCode.BadRequest);
    lateBody.Should().Contain("before this step");
  }

  [Fact]
  public async Task ALearnWithBothOrNeitherOfRegionAndPositionIsRejectedWith400() {
    using var app = CreateFactory();
    var client = AuthedClient(app);
    var lookup = Step("lookup-1", "quiz-lookup", LookupPayload());

    var (both, bothBody) = await SaveAsync(client, lookup,
      Step("learn-1", "quiz-learn", new { lookupStepId = "lookup-1", correctPosition = 1, correctRegion = Region(10) }));
    var (neither, neitherBody) = await SaveAsync(client, lookup,
      Step("learn-1", "quiz-learn", new { lookupStepId = "lookup-1" }));

    both.Should().Be(HttpStatusCode.BadRequest);
    bothBody.Should().Contain("exactly one");
    neither.Should().Be(HttpStatusCode.BadRequest);
    neitherBody.Should().Contain("exactly one");
  }

  [Fact]
  public async Task ALearnWithAPositionBelowOneIsRejectedWith400() {
    using var app = CreateFactory();
    var client = AuthedClient(app);

    var (status, body) = await SaveAsync(client,
      Step("lookup-1", "quiz-lookup", LookupPayload()),
      Step("learn-1", "quiz-learn", new { lookupStepId = "lookup-1", correctPosition = 0 }));

    status.Should().Be(HttpStatusCode.BadRequest);
    body.Should().Contain("correctPosition");
  }

  [Fact]
  public async Task ARejectedStepWithoutACategoryLeavesNoRecord() {
    // SC-007: no record without a category exists. A step without category does not save, and the store has no record
    // for its game. Every record that exists has a category.
    using var app = CreateFactory();
    var client = AuthedClient(app);
    var game = "sc007-" + Guid.NewGuid().ToString("N");
    var payload = LookupPayload(game);
    payload.Remove("category");

    var (status, _) = await SaveAsync(client, Step("lookup-1", "quiz-lookup", payload));

    status.Should().Be(HttpStatusCode.BadRequest);
    var repo = app.Services.GetRequiredService<IQuizAnswerRepository>();
    (await repo.ListAsync(null, game, null).ConfigureAwait(false)).Should().BeEmpty();
    (await repo.ListAsync(null, null, null).ConfigureAwait(false)).Where(r => string.IsNullOrWhiteSpace(r.Category)).Should().BeEmpty();
  }
}
