using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace GameBot.IntegrationTests.Sequences;

/// <summary>
/// Integration tests for feature 081 (FR-001, Part B): a <c>commandOutcome</c> condition's
/// <c>stepRef</c> may reference any step reachable from the sequence root — including one nested
/// inside a <c>Loop</c>/<c>If</c> body other than the condition's own — not only an immediate
/// sibling, and can query a <c>Break</c> step's fired/not-fired outcome (<c>break</c>/<c>no_break</c>).
/// Before this feature, creation of these sequences was rejected with
/// <c>400 "references unknown prior step"</c>.
///
/// Break/no-break is controlled deterministically via a plain dispatching command step's
/// <c>success</c> outcome and a mismatching <c>expectedState</c> on the Break's own condition —
/// deliberately avoiding <c>imageVisible</c>, which requires a capture session started via
/// <c>POST /api/sessions/start</c> to ever evaluate true (see pns-account-state / gamebot-api-notes
/// memory: a plain <c>POST /api/sessions</c> session always reports images absent).
/// </summary>
[Collection("ConfigIsolation")]
public sealed class NestedStepOutcomeReferenceIntegrationTests : IDisposable {
  private readonly string? _prevUseAdb;
  private readonly string? _prevDynamicPort;
  private readonly string? _prevAuthToken;
  private readonly string? _prevDataDir;

  public NestedStepOutcomeReferenceIntegrationTests() {
    _prevUseAdb = Environment.GetEnvironmentVariable("GAMEBOT_USE_ADB");
    _prevDynamicPort = Environment.GetEnvironmentVariable("GAMEBOT_DYNAMIC_PORT");
    _prevAuthToken = Environment.GetEnvironmentVariable("GAMEBOT_AUTH_TOKEN");
    _prevDataDir = Environment.GetEnvironmentVariable("GAMEBOT_DATA_DIR");

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
    GC.SuppressFinalize(this);
  }

  // A go-to-home-screen primitive step always dispatches in stub mode (GAMEBOT_USE_ADB=false),
  // giving a deterministic "success" outcome with no image/session dependency.
  private static object DispatchingStep(string stepId, object? condition = null) => new {
    stepId,
    stepType = "Action",
    condition,
    primitiveAction = new { type = "go-to-home-screen", schemaVersion = "v1", payload = new { } }
  };

  private static object CommandOutcomeCondition(string stepRef, string expectedState) => new {
    type = "commandOutcome",
    stepRef,
    expectedState
  };

  private static async Task<string> CreateSessionAsync(HttpClient client, string gameName) {
    var gameResp = await client.PostAsJsonAsync(new Uri("/api/games", UriKind.Relative), new { name = gameName, description = "desc" }).ConfigureAwait(false);
    gameResp.EnsureSuccessStatusCode();
    var game = await gameResp.Content.ReadFromJsonAsync<Dictionary<string, object>>().ConfigureAwait(false);
    var gameId = game!["id"]!.ToString();

    var sessionResp = await client.PostAsJsonAsync(new Uri("/api/sessions", UriKind.Relative), new { gameId }).ConfigureAwait(false);
    sessionResp.EnsureSuccessStatusCode();
    var session = await sessionResp.Content.ReadFromJsonAsync<Dictionary<string, object>>().ConfigureAwait(false);
    return session!["id"]!.ToString()!;
  }

  private static async Task<(HttpStatusCode CreateStatus, JsonElement? CreateBody, JsonElement? Execution)> CreateAndExecuteAsync(
      HttpClient client, string name, object[] steps) {
    var sessionId = await CreateSessionAsync(client, name + "-game").ConfigureAwait(false);

    var createResponse = await client.PostAsJsonAsync("/api/sequences", new { name, version = 1, steps }).ConfigureAwait(false);
    var createBody = await createResponse.Content.ReadFromJsonAsync<JsonElement>().ConfigureAwait(false);
    if (createResponse.StatusCode != HttpStatusCode.Created) {
      return (createResponse.StatusCode, createBody, null);
    }

    var sequenceId = createBody.GetProperty("id").GetString();
    var executeResponse = await client.PostAsJsonAsync($"/api/sequences/{sequenceId}/execute", new { sessionId }).ConfigureAwait(false);
    executeResponse.StatusCode.Should().Be(HttpStatusCode.OK);
    var execution = await executeResponse.Content.ReadFromJsonAsync<JsonElement>().ConfigureAwait(false);
    return (createResponse.StatusCode, createBody, execution);
  }

  private static object[] BreakInLoopThenTopLevelGateSteps(string breakExpectedState) => new object[] {
    DispatchingStep("probe"),
    new {
      stepId = "loop-1",
      stepType = "Loop",
      loop = new { loopType = "count", count = 1, maxIterations = 1 },
      body = new object[] {
        new {
          stepId = "cluster-not-found-break",
          stepType = "Break",
          breakCondition = CommandOutcomeCondition("probe", breakExpectedState)
        }
      }
    },
    DispatchingStep("gate", CommandOutcomeCondition("cluster-not-found-break", "break"))
  };

  [Fact] // T014 (US2)
  public async Task CreatingSequenceWithCrossScopeBreakReferenceIsAcceptedNotRejected() {
    using var app = new WebApplicationFactory<Program>();
    var client = app.CreateClient();
    client.DefaultRequestHeaders.Add("Authorization", "Bearer test-token");

    // probe's outcome is "success"; expectedState "success" makes the break fire — but the
    // acceptance being tested here is creation, not the break firing.
    var (createStatus, createBody, _) = await CreateAndExecuteAsync(
        client, "cross-scope-break-ref-accepted", BreakInLoopThenTopLevelGateSteps("success")).ConfigureAwait(false);

    createStatus.Should().Be(HttpStatusCode.Created);
    createBody!.Value.TryGetProperty("errors", out _).Should().BeFalse();
  }

  [Fact] // T014 (US2)
  public async Task CrossScopeBreakReferenceResolvesToBreakWhenTheBreakFires() {
    using var app = new WebApplicationFactory<Program>();
    var client = app.CreateClient();
    client.DefaultRequestHeaders.Add("Authorization", "Bearer test-token");

    // Break's own condition: commandOutcome{stepRef:"probe", expectedState:"success"} — probe
    // dispatches successfully ("success"), so this matches and the break fires.
    var (_, _, execution) = await CreateAndExecuteAsync(
        client, "cross-scope-break-fires", BreakInLoopThenTopLevelGateSteps("success")).ConfigureAwait(false);

    var steps = execution!.Value.GetProperty("steps");
    var gate = FindStep(steps, "gate");
    gate.GetProperty("status").GetString().Should().Be("Succeeded");
    gate.GetProperty("actionOutcome").GetString().Should().NotBe("skipped");
  }

  [Fact] // T014 (US2)
  public async Task CrossScopeBreakReferenceResolvesToNoBreakWhenTheBreakDoesNotFire() {
    using var app = new WebApplicationFactory<Program>();
    var client = app.CreateClient();
    client.DefaultRequestHeaders.Add("Authorization", "Bearer test-token");

    // Break's own condition: commandOutcome{stepRef:"probe", expectedState:"failed"} — probe's
    // actual outcome is "success" (≠ "failed"), so the break does not fire.
    var (_, _, execution) = await CreateAndExecuteAsync(
        client, "cross-scope-break-no-fire", BreakInLoopThenTopLevelGateSteps("failed")).ConfigureAwait(false);

    var steps = execution!.Value.GetProperty("steps");
    var gate = FindStep(steps, "gate");
    gate.GetProperty("status").GetString().Should().Be("Skipped");
    gate.GetProperty("actionOutcome").GetString().Should().Be("skipped");
  }

  [Fact] // T015 (US2) — a commandOutcome condition referencing a Break step within its OWN loop
         // body (already validation-legal before this feature) now actually resolves at runtime,
         // instead of always failing with "reference unavailable" (the Break outcome was never
         // recorded for lookup before this feature).
  public async Task CommandOutcomeReferencingBreakWithinItsOwnLoopBodyResolvesAtRuntime() {
    using var app = new WebApplicationFactory<Program>();
    var client = app.CreateClient();
    client.DefaultRequestHeaders.Add("Authorization", "Bearer test-token");

    var steps = new object[] {
      DispatchingStep("probe"),
      new {
        stepId = "loop-1",
        stepType = "Loop",
        loop = new { loopType = "count", count = 2, maxIterations = 2 },
        body = new object[] {
          new {
            stepId = "brk",
            stepType = "Break",
            // probe's outcome is "success"; expecting "failed" never matches, so the break never
            // fires and every iteration records "no_break".
            breakCondition = CommandOutcomeCondition("probe", "failed")
          },
          DispatchingStep("same-body-gate", CommandOutcomeCondition("brk", "no_break"))
        }
      }
    };

    var (createStatus, _, execution) = await CreateAndExecuteAsync(
        client, "same-body-break-ref", steps).ConfigureAwait(false);

    createStatus.Should().Be(HttpStatusCode.Created);
    execution!.Value.GetProperty("status").GetString().Should().Be("Succeeded");

    var allSteps = execution.Value.GetProperty("steps");
    var sameBodyGates = new List<JsonElement>();
    foreach (var s in allSteps.EnumerateArray()) {
      if (s.TryGetProperty("commandId", out var id) && id.GetString() == "same-body-gate") {
        sameBodyGates.Add(s);
      }
    }
    sameBodyGates.Should().HaveCount(2);
    sameBodyGates.Should().OnlyContain(s => s.GetProperty("status").GetString() == "Succeeded");
  }

  [Fact] // T016 (US2) — FR-011 regression: a stepRef that is validation-legal (reachable + prior)
         // but names a step that did NOT execute this run (an If branch not taken) still fails the
         // referencing step and the sequence with the existing "unavailable" error — widening
         // reference scope must not introduce a new silent-success/silent-skip path.
  public async Task ReferencingAStepFromTheNotTakenIfBranchStillFailsHard() {
    using var app = new WebApplicationFactory<Program>();
    var client = app.CreateClient();
    client.DefaultRequestHeaders.Add("Authorization", "Bearer test-token");

    var steps = new object[] {
      DispatchingStep("probe"),
      new {
        stepId = "if-1",
        stepType = "If",
        // probe's outcome is "success"; expecting "failed" is false, so the else (empty) branch
        // runs and "sometimes-step" (in the then branch) never executes this run.
        @if = new { condition = CommandOutcomeCondition("probe", "failed") },
        body = new object[] { DispatchingStep("sometimes-step") },
        elseBody = Array.Empty<object>()
      },
      DispatchingStep("gate", CommandOutcomeCondition("sometimes-step", "success"))
    };

    var (createStatus, _, execution) = await CreateAndExecuteAsync(
        client, "not-taken-branch-ref", steps).ConfigureAwait(false);

    // Structurally reachable and prior, so creation succeeds (this is what feature 081 changes) —
    // but the reference is unresolvable at runtime because "sometimes-step" never ran this time.
    createStatus.Should().Be(HttpStatusCode.Created);
    execution!.Value.GetProperty("status").GetString().Should().Be("Failed");
    var gate = FindStep(execution.Value.GetProperty("steps"), "gate");
    gate.GetProperty("message").GetString().Should().Contain("unavailable");
  }

  [Fact] // T022 (US3) — the whole-tree resolution mechanism built for US2 generalizes to an
         // ordinary (non-Break) step nested inside a different Loop's body, using the existing
         // success/failed/skipped vocabulary exactly as a top-level reference does today.
  public async Task CrossScopeReferenceToNonBreakStepNestedInADifferentLoopResolvesAtRuntime() {
    using var app = new WebApplicationFactory<Program>();
    var client = app.CreateClient();
    client.DefaultRequestHeaders.Add("Authorization", "Bearer test-token");

    var steps = new object[] {
      new {
        stepId = "loop-1",
        stepType = "Loop",
        loop = new { loopType = "count", count = 1, maxIterations = 1 },
        body = new object[] { DispatchingStep("inner-command") }
      },
      DispatchingStep("gate", CommandOutcomeCondition("inner-command", "success"))
    };

    var (createStatus, _, execution) = await CreateAndExecuteAsync(
        client, "non-break-nested-ref", steps).ConfigureAwait(false);

    createStatus.Should().Be(HttpStatusCode.Created);
    execution!.Value.GetProperty("status").GetString().Should().Be("Succeeded");
    var gate = FindStep(execution.Value.GetProperty("steps"), "gate");
    gate.GetProperty("status").GetString().Should().Be("Succeeded");
    gate.GetProperty("actionOutcome").GetString().Should().NotBe("skipped");
  }

  private static JsonElement FindStep(JsonElement steps, string stepId) {
    foreach (var step in steps.EnumerateArray()) {
      if (step.TryGetProperty("commandId", out var id) && id.GetString() == stepId) {
        return step;
      }
    }
    throw new InvalidOperationException($"No step with commandId '{stepId}' found in execution result.");
  }
}
