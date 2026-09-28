using System;
using System.Linq;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace GameBot.ContractTests.ExecutionLogs;

/// <summary>
/// Issue #232 (spec 110): the execution log entry of a guarded Loop step shows the condition type
/// and the condition result, as the entries of If steps and conditioned Action steps do. Before the
/// fix, the entry showed only "Loop '...' true after N iterations".
/// <para>
/// A <c>lastRun</c> condition is false in an ad-hoc run, so the guard is false without a device,
/// and <c>none(lastRun)</c> is true.
/// </para>
/// </summary>
public sealed class ExecutionLogsLoopGuardContractTests : IDisposable {
  private readonly string? _prevToken;
  private readonly string? _prevAdb;

  public ExecutionLogsLoopGuardContractTests() {
    _prevToken = Environment.GetEnvironmentVariable("GAMEBOT_AUTH_TOKEN");
    _prevAdb = Environment.GetEnvironmentVariable("GAMEBOT_USE_ADB");
    Environment.SetEnvironmentVariable("GAMEBOT_AUTH_TOKEN", "test-token");
    Environment.SetEnvironmentVariable("GAMEBOT_USE_ADB", "false");
  }

  public void Dispose() {
    Environment.SetEnvironmentVariable("GAMEBOT_AUTH_TOKEN", _prevToken);
    Environment.SetEnvironmentVariable("GAMEBOT_USE_ADB", _prevAdb);
    GC.SuppressFinalize(this);
  }

  private static object LastRunLeaf() => new { type = "lastRun", sequence = "self", status = "success", within = "01:00:00" };

  private static object GuardedLoopSequence(string name, object guard) => new {
    name,
    version = 1,
    steps = new object[] {
      new {
        stepId = "guarded-loop",
        stepType = "Loop",
        condition = guard,
        loop = new { loopType = "count", count = 2 },
        body = new object[] {
          new {
            stepId = "inner",
            primitiveAction = new { type = "tap", schemaVersion = "v1", payload = new { x = 10, y = 10 } }
          }
        }
      }
    }
  };

  [Fact]
  public async Task SkippedLoopEntryCarriesFalseConditionResult() {
    var loop = await RunAndReadLoopAttributesAsync(LastRunLeaf()).ConfigureAwait(false);

    loop.GetProperty("status").GetString().Should().Be("Skipped");
    loop.GetProperty("iterations").GetInt32().Should().Be(0);
    loop.GetProperty("conditionType").GetString().Should().Be("lastRun");
    loop.GetProperty("conditionResult").GetString().Should().Be("false");
  }

  [Fact]
  public async Task GuardedLoopEntryCarriesTrueConditionResult() {
    var guard = new { type = "none", children = new[] { LastRunLeaf() } };
    var loop = await RunAndReadLoopAttributesAsync(guard).ConfigureAwait(false);

    loop.GetProperty("iterations").GetInt32().Should().Be(2);
    loop.GetProperty("conditionType").GetString().Should().Be("none");
    loop.GetProperty("conditionResult").GetString().Should().Be("true");
  }

  private static async Task<JsonElement> RunAndReadLoopAttributesAsync(object guard) {
    using var app = new WebApplicationFactory<Program>();
    var client = app.CreateClient();
    client.DefaultRequestHeaders.Add("Authorization", "Bearer test-token");

    var name = $"log-loop-guard-{Guid.NewGuid():N}";
    var created = await client.PostAsJsonAsync("/api/sequences", GuardedLoopSequence(name, guard)).ConfigureAwait(false);
    var createdBody = await created.Content.ReadAsStringAsync().ConfigureAwait(false);
    created.StatusCode.Should().Be(HttpStatusCode.Created, createdBody);
    var sequenceId = JsonDocument.Parse(createdBody).RootElement.GetProperty("id").GetString()!;

    // dryRun: the run needs no session, and it walks the real loop without a device.
    var executed = await client.PostAsJsonAsync(
      $"/api/sequences/{sequenceId}/execute", new { dryRun = true }).ConfigureAwait(false);
    executed.StatusCode.Should().Be(HttpStatusCode.OK, await executed.Content.ReadAsStringAsync().ConfigureAwait(false));

    var executionId = await FindExecutionIdAsync(client, sequenceId).ConfigureAwait(false);
    var detail = await client.GetAsync(new Uri($"/api/execution-logs/{executionId}", UriKind.Relative)).ConfigureAwait(false);
    var detailBody = await detail.Content.ReadAsStringAsync().ConfigureAwait(false);
    detail.StatusCode.Should().Be(HttpStatusCode.OK, detailBody);

    using var doc = JsonDocument.Parse(detailBody);
    var loopDetail = doc.RootElement.GetProperty("details").EnumerateArray().FirstOrDefault(item =>
      item.TryGetProperty("attributes", out var attributes)
      && attributes.ValueKind == JsonValueKind.Object
      && attributes.TryGetProperty("stepType", out var stepType)
      && stepType.GetString() == "loop");
    loopDetail.ValueKind.Should().Be(JsonValueKind.Object, "the log has a detail item for the loop step: {0}", detailBody);

    var loop = loopDetail.GetProperty("attributes").Clone();
    loop.TryGetProperty("conditionType", out _).Should().BeTrue("the loop entry has the condition type: {0}", detailBody);
    loop.TryGetProperty("conditionResult", out _).Should().BeTrue("the loop entry has the condition result: {0}", detailBody);
    return loop;
  }

  private static async Task<string> FindExecutionIdAsync(System.Net.Http.HttpClient client, string sequenceId) {
    var list = await client.GetAsync(new Uri("/api/execution-logs?limit=50", UriKind.Relative)).ConfigureAwait(false);
    var body = await list.Content.ReadAsStringAsync().ConfigureAwait(false);
    list.StatusCode.Should().Be(HttpStatusCode.OK, body);

    using var doc = JsonDocument.Parse(body);
    foreach (var item in doc.RootElement.GetProperty("items").EnumerateArray()) {
      if (item.TryGetProperty("objectRef", out var objectRef)
          && objectRef.ValueKind == JsonValueKind.Object
          && objectRef.TryGetProperty("objectId", out var refId)
          && refId.GetString() == sequenceId) {
        return item.GetProperty("id").GetString()!;
      }
    }

    throw new InvalidOperationException($"No execution log entry for sequence '{sequenceId}'. Body: {body}");
  }
}
