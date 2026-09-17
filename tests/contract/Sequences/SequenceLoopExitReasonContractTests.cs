using System;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace GameBot.ContractTests.Sequences;

/// <summary>
/// A loop step's <c>exitReason</c> on the run response (feature 081, guarded by feature 103 /
/// issue #193, FR-011a).
/// <para>
/// This file exists because of a wrong turn worth recording. Reading feature 103's research, the
/// absence of <c>ExitReason</c> anywhere in <c>GameBot.Service</c> looked like proof that the loop
/// exit reason was never published over the API. It is not: <c>ExecuteSequenceAsync</c> ends in
/// <c>Results.Ok(res)</c> where <c>res</c> is the <em>domain</em> run result, so
/// <c>System.Text.Json</c> serializes <c>exitReason</c> with no DTO in between. The absence of a
/// mapping is the reason there is nothing to map, not evidence of a gap.
/// </para>
/// <para>
/// That also makes this surface fragile in a specific way: nothing in the service layer mentions
/// these field names, so a rename or a DTO introduced later would silently drop them from a
/// published response. This test is the tripwire for that.
/// </para>
/// </summary>
public sealed class SequenceLoopExitReasonContractTests {
  private static WebApplicationFactory<Program> CreateFactory() {
    Environment.SetEnvironmentVariable("GAMEBOT_USE_ADB", "false");
    Environment.SetEnvironmentVariable("GAMEBOT_DYNAMIC_PORT", "true");
    Environment.SetEnvironmentVariable("GAMEBOT_AUTH_TOKEN", "test-token");
    return new WebApplicationFactory<Program>();
  }

  private static System.Net.Http.HttpClient Client(WebApplicationFactory<Program> app) {
    var client = app.CreateClient();
    client.DefaultRequestHeaders.Add("Authorization", "Bearer test-token");
    return client;
  }

  /// <summary>A count loop with a plain body, so the run completes without a break or a ceiling.</summary>
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
  public async Task TheRunResponseCarriesExitReasonOnALoopStepUnderExactlyThoseFieldNames() {
    using var app = CreateFactory();
    var client = Client(app);

    var created = await client.PostAsJsonAsync("/api/sequences", LoopSequencePayload($"exit-reason-{Guid.NewGuid():N}")).ConfigureAwait(false);
    var createdBody = await created.Content.ReadAsStringAsync().ConfigureAwait(false);
    created.StatusCode.Should().Be(HttpStatusCode.Created, createdBody);

    var sequenceId = JsonDocument.Parse(createdBody).RootElement.GetProperty("id").GetString();

    // dryRun walks the real step tree — including the loop's iteration counting and exit-reason
    // bookkeeping — without dispatching to a device, so the run needs no session. Dispatching a tap
    // without one fails the step, which fails the loop, and a failed loop is recorded with no exit
    // reason at all; that would test the wrong thing.
    var executed = await client.PostAsJsonAsync(
      $"/api/sequences/{sequenceId}/execute", new { dryRun = true }).ConfigureAwait(false);
    var executedBody = await executed.Content.ReadAsStringAsync().ConfigureAwait(false);
    executed.StatusCode.Should().Be(HttpStatusCode.OK, executedBody);

    using var doc = JsonDocument.Parse(executedBody);
    var loopStep = FindLoopStep(doc.RootElement);

    // A loop that fails early is recorded without an exit reason at all, so this assertion comes
    // first: without it, a body step failing for an unrelated reason would present as a missing
    // exit reason and send the next reader hunting the wrong bug.
    loopStep.GetProperty("status").GetString()
      .Should().NotBe("Failed", "the loop must complete for its exit reason to be meaningful: {0}", executedBody);

    loopStep.TryGetProperty("exitReason", out var exitReason)
      .Should().BeTrue("a loop step's run result publishes its exit reason: {0}", executedBody);

    // Named individually rather than as a shape assertion: a consumer reads these two keys, and a
    // rename is exactly the regression this file guards.
    exitReason.TryGetProperty("brokeVia", out var brokeVia).Should().BeTrue();
    exitReason.TryGetProperty("exhaustedMaxIterations", out var exhausted).Should().BeTrue();

    // A count loop that ran to completion with no break is the "neither" state — the one that is
    // easiest to render wrongly as one of the other two.
    brokeVia.ValueKind.Should().Be(JsonValueKind.Null);
    exhausted.GetBoolean().Should().BeFalse();
  }

  private static JsonElement FindLoopStep(JsonElement root) {
    foreach (var step in root.GetProperty("steps").EnumerateArray()) {
      if (step.TryGetProperty("loopIterations", out var iterations) && iterations.ValueKind == JsonValueKind.Array) {
        return step;
      }
    }

    throw new InvalidOperationException("The run response contained no loop step.");
  }
}
