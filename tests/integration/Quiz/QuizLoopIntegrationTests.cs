using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using GameBot.Domain.Commands.SelfReschedule;
using GameBot.Domain.Quiz;
using GameBot.Service.Services.Quiz;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

#pragma warning disable CA2007, CA1861, CA2000

namespace GameBot.IntegrationTests.Quiz;

/// <summary>
/// Feature 142: the whole loop through the real service. A sequence with a lookup step and a learn step runs
/// three times with a screen reader that returns fixed texts. The first run guesses and learns. The second run knows the
/// answer, also when the answers are in a new order. After a delete, the third run guesses again.
/// </summary>
[Collection("ConfigIsolation")]
public sealed class QuizLoopIntegrationTests : IDisposable {
  private readonly string? _prevUseAdb;
  private readonly string? _prevDynamicPort;
  private readonly string? _prevAuthToken;
  private readonly string? _prevDataDir;
  private readonly string? _prevStorageRoot;

  public QuizLoopIntegrationTests() {
    _prevUseAdb = Environment.GetEnvironmentVariable("GAMEBOT_USE_ADB");
    _prevDynamicPort = Environment.GetEnvironmentVariable("GAMEBOT_DYNAMIC_PORT");
    _prevAuthToken = Environment.GetEnvironmentVariable("GAMEBOT_AUTH_TOKEN");
    _prevDataDir = Environment.GetEnvironmentVariable("GAMEBOT_DATA_DIR");
    _prevStorageRoot = Environment.GetEnvironmentVariable("Service__Storage__Root");

    Environment.SetEnvironmentVariable("GAMEBOT_USE_ADB", "false");
    Environment.SetEnvironmentVariable("GAMEBOT_DYNAMIC_PORT", "true");
    Environment.SetEnvironmentVariable("GAMEBOT_AUTH_TOKEN", "test-token");
    TestEnvironment.PrepareCleanDataDir();
  }

  public void Dispose() {
    Environment.SetEnvironmentVariable("GAMEBOT_USE_ADB", _prevUseAdb);
    Environment.SetEnvironmentVariable("GAMEBOT_DYNAMIC_PORT", _prevDynamicPort);
    Environment.SetEnvironmentVariable("GAMEBOT_AUTH_TOKEN", _prevAuthToken);
    Environment.SetEnvironmentVariable("GAMEBOT_DATA_DIR", _prevDataDir);
    Environment.SetEnvironmentVariable("Service__Storage__Root", _prevStorageRoot);
    GC.SuppressFinalize(this);
  }

  private sealed class QueueReader : IQuizScreenReader {
    private readonly Queue<string[]> _reads = new();

    public void Enqueue(params string[] texts) => _reads.Enqueue(texts);

    public IReadOnlyList<string?>? ReadRegions(string? sessionId, IReadOnlyList<OcrOffsetRegion> regions) =>
      _reads.Count > 0 ? _reads.Dequeue() : null;
  }

  private static object Region(int y) => new { x = 40, y, width = 400, height = 60 };

  private static object Step(string id, string type, object payload) => new {
    stepId = id,
    label = id,
    stepType = "Action",
    primitiveAction = new { type, schemaVersion = "1", payload }
  };

  private static async Task<JsonElement> RunAsync(HttpClient client, string sequenceId) {
    var response = await client.PostAsJsonAsync($"/api/sequences/{sequenceId}/execute", new { }).ConfigureAwait(false);
    var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
    response.StatusCode.Should().Be(HttpStatusCode.OK, body);
    return JsonDocument.Parse(body).RootElement.Clone();
  }

  [Fact]
  public async Task GuessLearnKnownAndDeleteThroughTheRealService() {
    var reader = new QueueReader();
    using var app = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
      builder.ConfigureServices(services => {
        services.RemoveAll<IQuizScreenReader>();
        services.AddSingleton<IQuizScreenReader>(reader);
      }));
    using var client = app.CreateClient();
    client.DefaultRequestHeaders.Add("Authorization", "Bearer test-token");
    var game = "loop-" + Guid.NewGuid().ToString("N");

    var create = await client.PostAsJsonAsync("/api/sequences", new {
      name = "quiz-loop-" + Guid.NewGuid().ToString("N"),
      version = 1,
      steps = new[] {
        Step("lookup-1", "quiz-lookup", new {
          game, category = "radio", store = "loop-store",
          question = Region(200), answers = new[] { Region(320), Region(400), Region(480) }
        }),
        Step("learn-1", "quiz-learn", new { lookupStepId = "lookup-1", correctPosition = 2 })
      }
    }).ConfigureAwait(false);
    var created = await create.Content.ReadAsStringAsync().ConfigureAwait(false);
    create.StatusCode.Should().Be(HttpStatusCode.Created, created);
    var sequenceId = JsonDocument.Parse(created).RootElement.GetProperty("id").GetString()!;

    // Run 1: the question is not known. The lookup guesses, and the learn step stores answer 2 (Mars).
    reader.Enqueue("Which planet is red?", "Venus", "Mars", "Earth");
    var first = await RunAsync(client, sequenceId).ConfigureAwait(false);
    first.GetProperty("status").GetString().Should().Be("Succeeded");
    var firstSteps = first.GetProperty("steps").EnumerateArray().ToList();
    firstSteps[0].GetProperty("actionOutcome").GetString().Should().StartWith("pick_");
    firstSteps[0].GetProperty("message").GetString().Should().StartWith("guess");
    firstSteps[1].GetProperty("actionOutcome").GetString().Should().Be("stored");

    var records = await client.GetFromJsonAsync<JsonElement>($"/api/quiz/records?game={game}").ConfigureAwait(false);
    records.GetArrayLength().Should().Be(1);
    var record = records[0];
    record.GetProperty("question").GetString().Should().Be("which planet is red");
    record.GetProperty("answer").GetString().Should().Be("mars");
    var recordId = record.GetProperty("id").GetString()!;

    // Run 2: the answers are in a new order. The lookup knows the answer: position 3.
    reader.Enqueue("Which planet is red?", "Earth", "Venus", "Mars");
    var second = await RunAsync(client, sequenceId).ConfigureAwait(false);
    var secondLookup = second.GetProperty("steps").EnumerateArray().First();
    secondLookup.GetProperty("actionOutcome").GetString().Should().Be("pick_3");
    secondLookup.GetProperty("message").GetString().Should().StartWith("known");

    // The record is on disk: a new repository on the same data folder finds it.
    var repo = app.Services.GetRequiredService<IQuizAnswerRepository>();
    (await repo.FindBestAsync("loop-store", game, "radio", "which planet is red", 0.85).ConfigureAwait(false)).Should().NotBeNull();

    // A delete by the operator makes the next lookup a guess again.
    var delete = await client.DeleteAsync(new Uri($"/api/quiz/records/{recordId}", UriKind.Relative)).ConfigureAwait(false);
    delete.StatusCode.Should().Be(HttpStatusCode.NoContent);
    reader.Enqueue("Which planet is red?", "Earth", "Venus", "Mars");
    var third = await RunAsync(client, sequenceId).ConfigureAwait(false);
    third.GetProperty("steps").EnumerateArray().First().GetProperty("message").GetString().Should().StartWith("guess");
  }

  [Fact]
  public async Task AnUnreadableScreenGivesTheOutcomeAndTheLearnStepIsSkipped() {
    var reader = new QueueReader();
    using var app = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
      builder.ConfigureServices(services => {
        services.RemoveAll<IQuizScreenReader>();
        services.AddSingleton<IQuizScreenReader>(reader);
      }));
    using var client = app.CreateClient();
    client.DefaultRequestHeaders.Add("Authorization", "Bearer test-token");
    var game = "unreadable-" + Guid.NewGuid().ToString("N");
    var create = await client.PostAsJsonAsync("/api/sequences", new {
      name = "quiz-unreadable-" + Guid.NewGuid().ToString("N"),
      version = 1,
      steps = new[] {
        Step("lookup-1", "quiz-lookup", new {
          game, category = "radio", store = "loop-store",
          question = Region(200), answers = new[] { Region(320), Region(400) }
        }),
        Step("learn-1", "quiz-learn", new { lookupStepId = "lookup-1", correctPosition = 1 })
      }
    }).ConfigureAwait(false);
    var sequenceId = JsonDocument.Parse(await create.Content.ReadAsStringAsync().ConfigureAwait(false)).RootElement.GetProperty("id").GetString()!;

    reader.Enqueue("   ", "A", "B");
    var run = await RunAsync(client, sequenceId).ConfigureAwait(false);

    run.GetProperty("status").GetString().Should().Be("Succeeded", "a quiz step never fails the sequence");
    var steps = run.GetProperty("steps").EnumerateArray().ToList();
    steps[0].GetProperty("actionOutcome").GetString().Should().Be("unreadable");
    steps[1].GetProperty("actionOutcome").GetString().Should().Be("skipped_no_match");
    (await client.GetFromJsonAsync<JsonElement>($"/api/quiz/records?game={game}").ConfigureAwait(false)).GetArrayLength().Should().Be(0);
  }
}
