using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using GameBot.Domain.Queues;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

#pragma warning disable CA2007, CA1861

namespace GameBot.ContractTests.Queues;

/// <summary>
/// Feature 105 (FR-005): <c>sequenceStats</c> on the queue detail responses. Each test creates its own
/// queue with a new ID, because the contract tests share one bin data folder.
/// </summary>
public sealed class QueueSequenceStatsContractTests {
  private static readonly DateTimeOffset T0 = new(2026, 9, 24, 12, 0, 3, TimeSpan.FromHours(2));

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

  /// <summary>
  /// The statistics file of the queue, in the folder of the store that the application uses. Other
  /// contract tests change GAMEBOT_DATA_DIR, so the path must come from the store and not from a guess.
  /// </summary>
  private static string StatsFile(WebApplicationFactory<Program> app, string queueId) =>
    Path.Combine(((FileSequenceRunStatisticsStore)app.Services.GetRequiredService<ISequenceRunStatisticsStore>()).FolderPath, queueId + ".json");

  private static async Task<string> CreateQueueAsync(HttpClient client) {
    var response = await client.PostAsJsonAsync(new Uri("/api/queues", UriKind.Relative),
      new { name = $"Stats-{Guid.NewGuid():N}", emulatorSerial = "emu-stats" }).ConfigureAwait(false);
    response.StatusCode.Should().Be(HttpStatusCode.Created);
    return JsonDocument.Parse(await response.Content.ReadAsStringAsync().ConfigureAwait(false))
      .RootElement.GetProperty("id").GetString()!;
  }

  private static async Task<string> CreateSequenceAsync(HttpClient client, string name) {
    var response = await client.PostAsJsonAsync(new Uri("/api/sequences", UriKind.Relative), new {
      name,
      version = 1,
      steps = new[] {
        new { stepId = "tap", primitiveAction = new { type = "tap", schemaVersion = "v1", payload = new { x = 5, y = 5 } } }
      }
    }).ConfigureAwait(false);
    var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
    response.StatusCode.Should().Be(HttpStatusCode.Created, body);
    return JsonDocument.Parse(body).RootElement.GetProperty("id").GetString()!;
  }

  private static async Task<JsonElement> GetDetailAsync(HttpClient client, string queueId) {
    var response = await client.GetAsync(new Uri($"/api/queues/{queueId}", UriKind.Relative)).ConfigureAwait(false);
    var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
    response.StatusCode.Should().Be(HttpStatusCode.OK, body);
    return JsonDocument.Parse(body).RootElement.Clone();
  }

  private static Task RecordAsync(WebApplicationFactory<Program> app, string queueId, string sequenceId, SequenceRunStatus status, int minute = 0) =>
    app.Services.GetRequiredService<ISequenceRunStatisticsStore>().RecordAsync(queueId, sequenceId, new SequenceRunRecord {
      StartedAt = T0.AddMinutes(minute),
      EndedAt = T0.AddMinutes(minute).AddSeconds(67),
      Status = status
    });

  [Fact]
  public async Task ANewQueueHasAnEmptyObject() {
    using var app = CreateFactory();
    var client = Client(app);
    var queueId = await CreateQueueAsync(client);

    var stats = (await GetDetailAsync(client, queueId)).GetProperty("sequenceStats");

    stats.ValueKind.Should().Be(JsonValueKind.Object);
    stats.EnumerateObject().Should().BeEmpty();
    await client.DeleteAsync(new Uri($"/api/queues/{queueId}", UriKind.Relative));
  }

  [Fact]
  public async Task ARecordedRunIsReturnedKeyedBySequenceIdInOrdinalOrder() {
    using var app = CreateFactory();
    var client = Client(app);
    var queueId = await CreateQueueAsync(client);
    var name = $"Stats seq {Guid.NewGuid():N}";
    var sequenceId = await CreateSequenceAsync(client, name);
    var missingId = "zz-missing-" + Guid.NewGuid().ToString("N");
    var upperId = "AA-missing-" + Guid.NewGuid().ToString("N");

    await RecordAsync(app, queueId, sequenceId, SequenceRunStatus.Success);
    await RecordAsync(app, queueId, sequenceId, SequenceRunStatus.Failure, 5);
    await RecordAsync(app, queueId, missingId, SequenceRunStatus.Cancelled);
    await RecordAsync(app, queueId, upperId, SequenceRunStatus.Success);

    var stats = (await GetDetailAsync(client, queueId)).GetProperty("sequenceStats");
    var keys = stats.EnumerateObject().Select(p => p.Name).ToList();
    keys.Should().Equal(keys.OrderBy(k => k, StringComparer.Ordinal));
    keys.Should().Contain(new[] { sequenceId, missingId, upperId });

    var entry = stats.GetProperty(sequenceId);
    entry.GetProperty("sequenceName").GetString().Should().Be(name);
    entry.GetProperty("lastRunStatus").GetString().Should().Be("failure");
    entry.GetProperty("lastRunStartedAt").GetDateTimeOffset().Should().Be(T0.AddMinutes(5));
    entry.GetProperty("lastRunEndedAt").GetDateTimeOffset().Should().Be(T0.AddMinutes(5).AddSeconds(67));
    entry.GetProperty("lastSuccessAt").GetDateTimeOffset().Should().Be(T0.AddSeconds(67));
    entry.GetProperty("successCount").GetInt64().Should().Be(1);
    entry.GetProperty("failureCount").GetInt64().Should().Be(1);
    entry.GetProperty("cancelledCount").GetInt64().Should().Be(0);
    entry.TryGetProperty("recentRuns", out _).Should().BeFalse("the history stays internal");

    var missing = stats.GetProperty(missingId);
    missing.GetProperty("sequenceName").ValueKind.Should().Be(JsonValueKind.Null);
    missing.GetProperty("lastRunStatus").GetString().Should().Be("cancelled");
    missing.GetProperty("lastSuccessAt").ValueKind.Should().Be(JsonValueKind.Null);

    await client.DeleteAsync(new Uri($"/api/queues/{queueId}", UriKind.Relative));
  }

  [Fact]
  public async Task EveryDetailResponseCarriesSequenceStats() {
    using var app = CreateFactory();
    var client = Client(app);
    var queueId = await CreateQueueAsync(client);
    await RecordAsync(app, queueId, "seq-put-paths", SequenceRunStatus.Success);

    var responses = new[] {
      await client.PutAsJsonAsync(new Uri($"/api/queues/{queueId}/entries", UriKind.Relative), new { sequenceIds = new[] { "other" } }),
      await client.PutAsJsonAsync(new Uri($"/api/queues/{queueId}/template", UriKind.Relative), new { templateId = (string?)null }),
      await client.PutAsJsonAsync(new Uri($"/api/queues/{queueId}/game", UriKind.Relative), new { gameId = (string?)null })
    };

    foreach (var response in responses) {
      var body = await response.Content.ReadAsStringAsync();
      response.StatusCode.Should().Be(HttpStatusCode.OK, body);
      // The entries PUT drops the sequence from the roster, but its entry stays (spec edge case
      // "Sequence removed from the queue template").
      JsonDocument.Parse(body).RootElement.GetProperty("sequenceStats")
        .GetProperty("seq-put-paths").GetProperty("successCount").GetInt64().Should().Be(1);
    }

    await client.DeleteAsync(new Uri($"/api/queues/{queueId}", UriKind.Relative));
  }

  [Fact]
  public async Task DeleteRemovesTheStatisticsFile() {
    using var app = CreateFactory();
    var client = Client(app);
    var queueId = await CreateQueueAsync(client);
    await RecordAsync(app, queueId, "seq-delete", SequenceRunStatus.Success);
    File.Exists(StatsFile(app, queueId)).Should().BeTrue();

    var response = await client.DeleteAsync(new Uri($"/api/queues/{queueId}", UriKind.Relative));

    response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    File.Exists(StatsFile(app, queueId)).Should().BeFalse();
  }

  [Fact]
  public async Task ADuplicateDoesNotCopyTheStatistics() {
    using var app = CreateFactory();
    var client = Client(app);
    var queueId = await CreateQueueAsync(client);
    await RecordAsync(app, queueId, "seq-dup", SequenceRunStatus.Success);

    var dup = await client.PostAsJsonAsync(new Uri($"/api/queues/{queueId}/duplicate", UriKind.Relative),
      new { name = $"Dup-{Guid.NewGuid():N}", emulatorSerial = "emu-stats" });
    var dupBody = await dup.Content.ReadAsStringAsync();
    dup.StatusCode.Should().Be(HttpStatusCode.Created, dupBody);
    var dupRoot = JsonDocument.Parse(dupBody).RootElement;
    dupRoot.TryGetProperty("sequenceStats", out _).Should().BeFalse("QueueResponse has no sequenceStats field");
    var newId = dupRoot.GetProperty("id").GetString()!;

    (await GetDetailAsync(client, newId)).GetProperty("sequenceStats").EnumerateObject().Should().BeEmpty();

    await client.DeleteAsync(new Uri($"/api/queues/{queueId}", UriKind.Relative));
    await client.DeleteAsync(new Uri($"/api/queues/{newId}", UriKind.Relative));
  }

  [Fact]
  public async Task ADamagedStatisticsFileGivesAnEmptyObjectAndNotAFiveHundred() {
    using var app = CreateFactory();
    var client = Client(app);
    var queueId = await CreateQueueAsync(client);
    Directory.CreateDirectory(Path.GetDirectoryName(StatsFile(app, queueId))!);
    await File.WriteAllTextAsync(StatsFile(app, queueId), "{ this is not json");

    var stats = (await GetDetailAsync(client, queueId)).GetProperty("sequenceStats");

    stats.EnumerateObject().Should().BeEmpty();
    await client.DeleteAsync(new Uri($"/api/queues/{queueId}", UriKind.Relative));
    File.Exists(StatsFile(app, queueId)).Should().BeFalse();
  }
}
