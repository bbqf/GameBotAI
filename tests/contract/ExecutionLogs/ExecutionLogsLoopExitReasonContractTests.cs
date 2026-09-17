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
/// A loop's exit reason in the <em>persisted run log</em> (feature 103, issue #193, FR-011).
/// <para>
/// The exit reason is returned on the response to a directly-invoked run, but that response is not
/// the record a queue-driven run leaves behind — a queue firing has no caller to answer, so its
/// execution log is the only trace. Before this feature the loop's log entry carried
/// <c>iterations</c>, <c>status</c> and a prose <c>message</c> and nothing structural about why the
/// loop stopped, so for precisely the runs that matter the exit reason was unreadable after the
/// fact. "Infer it from the message text" is the failure mode, not the fix.
/// </para>
/// <para>
/// The run here is invoked directly because that is the cheapest way to produce a real log entry;
/// the assertion is about the persisted entry, which a queue firing writes the same way.
/// </para>
/// </summary>
public sealed class ExecutionLogsLoopExitReasonContractTests : IDisposable {
  private readonly string? _prevToken;
  private readonly string? _prevAdb;

  public ExecutionLogsLoopExitReasonContractTests() {
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

  private static object LoopSequencePayload(string name) => new {
    name,
    version = 1,
    steps = new object[] {
      new {
        stepId = "loop1",
        stepType = "Loop",
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
  public async Task ThePersistedLogEntryForALoopStepCarriesItsExitReason() {
    using var app = new WebApplicationFactory<Program>();
    var client = app.CreateClient();
    client.DefaultRequestHeaders.Add("Authorization", "Bearer test-token");

    var name = $"log-exit-reason-{Guid.NewGuid():N}";
    var created = await client.PostAsJsonAsync("/api/sequences", LoopSequencePayload(name)).ConfigureAwait(false);
    var createdBody = await created.Content.ReadAsStringAsync().ConfigureAwait(false);
    created.StatusCode.Should().Be(HttpStatusCode.Created, createdBody);

    var sequenceId = JsonDocument.Parse(createdBody).RootElement.GetProperty("id").GetString();

    // dryRun so the run needs no session: it walks the real loop, counting iterations and recording
    // the exit reason, without dispatching to a device.
    var executed = await client.PostAsJsonAsync(
      $"/api/sequences/{sequenceId}/execute", new { dryRun = true }).ConfigureAwait(false);
    executed.StatusCode.Should().Be(HttpStatusCode.OK, await executed.Content.ReadAsStringAsync().ConfigureAwait(false));

    var executionId = await FindExecutionIdAsync(client, sequenceId!).ConfigureAwait(false);

    var detail = await client.GetAsync(new Uri($"/api/execution-logs/{executionId}", UriKind.Relative)).ConfigureAwait(false);
    var detailBody = await detail.Content.ReadAsStringAsync().ConfigureAwait(false);
    detail.StatusCode.Should().Be(HttpStatusCode.OK, detailBody);

    using var doc = JsonDocument.Parse(detailBody);
    var loopAttributes = FindLoopDetailAttributes(doc.RootElement, detailBody);

    // Beside the iteration count they belong with, so a reader sees "ran N times, stopped because X"
    // in one place.
    loopAttributes.TryGetProperty("iterations", out _).Should().BeTrue();

    loopAttributes.TryGetProperty("brokeVia", out var brokeVia)
      .Should().BeTrue("the loop's log entry records which Break fired: {0}", detailBody);
    loopAttributes.TryGetProperty("exhaustedMaxIterations", out var exhausted)
      .Should().BeTrue("the loop's log entry records whether the iteration ceiling was reached: {0}", detailBody);

    // This loop ran to completion with neither cause, which must be recorded as neither rather than
    // collapsed into one of the other two.
    brokeVia.ValueKind.Should().Be(JsonValueKind.Null);
    exhausted.GetBoolean().Should().BeFalse();
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

  private static JsonElement FindLoopDetailAttributes(JsonElement root, string body) {
    var loopDetail = root.GetProperty("details").EnumerateArray().FirstOrDefault(detail =>
      detail.TryGetProperty("attributes", out var attributes)
      && attributes.ValueKind == JsonValueKind.Object
      && attributes.TryGetProperty("stepType", out var stepType)
      && stepType.GetString() == "loop");

    loopDetail.ValueKind.Should().Be(JsonValueKind.Object, "the log records a detail item for the loop step: {0}", body);
    return loopDetail.GetProperty("attributes");
  }
}
