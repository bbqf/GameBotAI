using System;
using System.Linq;
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
/// Feature 142 (User Story 3): list, read, and delete quiz records. Records are made in the store with unique game names,
/// so the tests do not depend on the other records of the shared data folder.
/// </summary>
public sealed class QuizEndpointsContractTests {
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

  private static string Unique() => "g" + Guid.NewGuid().ToString("N");

  private static async Task<JsonElement> ListAsync(System.Net.Http.HttpClient client, string query) {
    var response = await client.GetAsync(new Uri("/api/quiz/records" + query, UriKind.Relative)).ConfigureAwait(false);
    response.StatusCode.Should().Be(HttpStatusCode.OK);
    return await response.Content.ReadFromJsonAsync<JsonElement>().ConfigureAwait(false);
  }

  [Fact]
  public async Task ListGivesTheRecordsWithTheirStoreName() {
    using var app = CreateFactory();
    var client = AuthedClient(app);
    var repo = app.Services.GetRequiredService<IQuizAnswerRepository>();
    var game = Unique();
    await repo.UpsertAsync("Store-A", game, "radio", "Which planet is red?", "Mars").ConfigureAwait(false);
    await repo.UpsertAsync("store-b", game, "history", "Capital of France?", "Paris").ConfigureAwait(false);

    var list = await ListAsync(client, $"?game={game}").ConfigureAwait(false);

    list.GetArrayLength().Should().Be(2);
    var first = list.EnumerateArray().Single(e => e.GetProperty("category").GetString() == "radio");
    first.GetProperty("store").GetString().Should().Be("Store-A");
    first.GetProperty("game").GetString().Should().Be(game);
    first.GetProperty("question").GetString().Should().Be("which planet is red");
    first.GetProperty("answer").GetString().Should().Be("mars");
    first.GetProperty("id").GetString().Should().MatchRegex("^[0-9a-f]{32}$");
    first.TryGetProperty("createdAt", out _).Should().BeTrue();
    first.TryGetProperty("updatedAt", out _).Should().BeTrue();
  }

  [Fact]
  public async Task ListFiltersCombineAndFollowTheCaseRules() {
    using var app = CreateFactory();
    var client = AuthedClient(app);
    var repo = app.Services.GetRequiredService<IQuizAnswerRepository>();
    var game = Unique();
    await repo.UpsertAsync("Store-A", game, "Radio", "q one", "a one").ConfigureAwait(false);
    await repo.UpsertAsync("Store-A", game, "history", "q two", "a two").ConfigureAwait(false);
    await repo.UpsertAsync("store-b", game, "radio", "q three", "a three").ConfigureAwait(false);

    (await ListAsync(client, $"?game={game}&store=STORE-a").ConfigureAwait(false)).GetArrayLength().Should().Be(2);
    (await ListAsync(client, $"?game={game}&category=RADIO").ConfigureAwait(false)).GetArrayLength().Should().Be(2);
    (await ListAsync(client, $"?game={game}&store=store-a&category=radio").ConfigureAwait(false)).GetArrayLength().Should().Be(1);
    (await ListAsync(client, $"?game={game.ToUpperInvariant()}").ConfigureAwait(false)).GetArrayLength().Should().Be(0, "game is an exact match");
    (await ListAsync(client, $"?game={game}&store=none").ConfigureAwait(false)).GetArrayLength().Should().Be(0);
  }

  [Fact]
  public async Task GetGivesOneRecordAnd404ForAnUnknownOrBadId() {
    using var app = CreateFactory();
    var client = AuthedClient(app);
    var repo = app.Services.GetRequiredService<IQuizAnswerRepository>();
    var created = await repo.UpsertAsync("s", Unique(), "radio", "q one", "a one").ConfigureAwait(false);

    var ok = await client.GetAsync(new Uri($"/api/quiz/records/{created.Record.Id}", UriKind.Relative)).ConfigureAwait(false);
    var unknown = await client.GetAsync(new Uri($"/api/quiz/records/{Guid.NewGuid():N}", UriKind.Relative)).ConfigureAwait(false);
    var bad = await client.GetAsync(new Uri("/api/quiz/records/not-a-valid-id", UriKind.Relative)).ConfigureAwait(false);

    ok.StatusCode.Should().Be(HttpStatusCode.OK);
    (await ok.Content.ReadFromJsonAsync<JsonElement>().ConfigureAwait(false)).GetProperty("id").GetString().Should().Be(created.Record.Id);
    unknown.StatusCode.Should().Be(HttpStatusCode.NotFound);
    (await unknown.Content.ReadAsStringAsync().ConfigureAwait(false)).Should().Contain("Quiz record not found.");
    bad.StatusCode.Should().Be(HttpStatusCode.NotFound);
  }

  [Fact]
  public async Task DeleteRemovesTheRecordAndGives404Afterwards() {
    using var app = CreateFactory();
    var client = AuthedClient(app);
    var repo = app.Services.GetRequiredService<IQuizAnswerRepository>();
    var game = Unique();
    var keep = await repo.UpsertAsync("s", game, "radio", "q keep", "a keep").ConfigureAwait(false);
    var drop = await repo.UpsertAsync("s", game, "radio", "q drop", "a drop").ConfigureAwait(false);

    var deleted = await client.DeleteAsync(new Uri($"/api/quiz/records/{drop.Record.Id}", UriKind.Relative)).ConfigureAwait(false);
    var again = await client.DeleteAsync(new Uri($"/api/quiz/records/{drop.Record.Id}", UriKind.Relative)).ConfigureAwait(false);
    var bad = await client.DeleteAsync(new Uri("/api/quiz/records/..%2F..%2Fx", UriKind.Relative)).ConfigureAwait(false);

    deleted.StatusCode.Should().Be(HttpStatusCode.NoContent);
    again.StatusCode.Should().Be(HttpStatusCode.NotFound);
    bad.StatusCode.Should().Be(HttpStatusCode.NotFound);
    var list = await ListAsync(client, $"?game={game}").ConfigureAwait(false);
    list.GetArrayLength().Should().Be(1);
    list[0].GetProperty("id").GetString().Should().Be(keep.Record.Id);
  }

  [Fact]
  public async Task AfterADeleteTheNextLookupIsAGuess() {
    using var app = CreateFactory();
    var client = AuthedClient(app);
    var repo = app.Services.GetRequiredService<IQuizAnswerRepository>();
    var game = Unique();
    var created = await repo.UpsertAsync("s", game, "radio", "q one", "a one").ConfigureAwait(false);
    (await repo.FindBestAsync("s", game, "radio", "q one", 0.85).ConfigureAwait(false)).Should().NotBeNull();

    await client.DeleteAsync(new Uri($"/api/quiz/records/{created.Record.Id}", UriKind.Relative)).ConfigureAwait(false);

    (await repo.FindBestAsync("s", game, "radio", "q one", 0.85).ConfigureAwait(false)).Should().BeNull();
  }

  [Fact]
  public async Task NoRouteCreatesOrChangesARecord() {
    using var app = CreateFactory();
    var client = AuthedClient(app);

    var post = await client.PostAsJsonAsync("/api/quiz/records", new { store = "s" }).ConfigureAwait(false);
    var put = await client.PutAsJsonAsync("/api/quiz/records/" + Guid.NewGuid().ToString("N"), new { answer = "x" }).ConfigureAwait(false);

    post.StatusCode.Should().BeOneOf(HttpStatusCode.NotFound, HttpStatusCode.MethodNotAllowed);
    put.StatusCode.Should().BeOneOf(HttpStatusCode.NotFound, HttpStatusCode.MethodNotAllowed);
  }

  [Fact]
  public async Task AnUnauthenticatedCallIsRefused() {
    using var app = CreateFactory();
    var client = app.CreateClient();

    var response = await client.GetAsync(new Uri("/api/quiz/records", UriKind.Relative)).ConfigureAwait(false);

    response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
  }
}
