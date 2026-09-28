using System;
using System.Linq;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using GameBot.Domain.Commands;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace GameBot.IntegrationTests.Sequences;

/// <summary>
/// Integration tests for issue #242 (B-028, feature 113). <c>POST /api/sequences</c> must reject a step
/// without <c>stepType</c> and without <c>primitiveAction</c> with a 400 that names the step. A dry run
/// must never store a sequence. The service must never store fewer steps or parameters than the
/// request declares.
/// </summary>
public sealed class SequenceCreateMalformedStepIntegrationTests {
  private const string CommandId = "probe-cmd";

  private static WebApplicationFactory<Program> CreateFactory() {
    Environment.SetEnvironmentVariable("GAMEBOT_USE_ADB", "false");
    Environment.SetEnvironmentVariable("GAMEBOT_DYNAMIC_PORT", "true");
    Environment.SetEnvironmentVariable("GAMEBOT_AUTH_TOKEN", "test-token");
    TestEnvironment.PrepareCleanDataDir();
    return new WebApplicationFactory<Program>();
  }

  private static async Task<HttpClient> CreateClientWithCommandAsync(WebApplicationFactory<Program> app) {
    var commandRepository = app.Services.GetRequiredService<ICommandRepository>();
    await commandRepository.AddAsync(new Command { Id = CommandId, Name = "Probe Command" }).ConfigureAwait(false);
    var client = app.CreateClient();
    client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "test-token");
    return client;
  }

  private static object RescheduleStep(string stepId) => new {
    stepId,
    primitiveAction = new { type = "reschedule-self", schemaVersion = "v1", payload = new { option = "AtQueueStart" } }
  };

  private static object CommandReferenceOnlyStep(string stepId) => new {
    stepId,
    commandReference = new { commandId = CommandId }
  };

  private static object CommandStep(string stepId) => new {
    stepId,
    stepType = "Action",
    primitiveAction = new { type = "command", schemaVersion = "v1", payload = new { commandId = CommandId } }
  };

  /// <summary>The reproduction body of issue #242.</summary>
  private static object ReproductionBody(string name, bool? dryRun = null) => dryRun is null
    ? new { name, steps = new[] { CommandReferenceOnlyStep("a"), RescheduleStep("b") } }
    : new { name, dryRun = dryRun.Value, steps = new[] { CommandReferenceOnlyStep("a"), RescheduleStep("b") } };

  private static async Task<JsonElement> ListSequencesAsync(HttpClient client) {
    var response = await client.GetAsync(new Uri("/api/sequences", UriKind.Relative)).ConfigureAwait(false);
    response.StatusCode.Should().Be(HttpStatusCode.OK);
    return await response.Content.ReadFromJsonAsync<JsonElement>().ConfigureAwait(false);
  }

  private static async Task<string[]> ErrorsAsync(HttpResponseMessage response) {
    var body = await response.Content.ReadFromJsonAsync<JsonElement>().ConfigureAwait(false);
    return body.GetProperty("errors").EnumerateArray().Select(e => e.GetString()!).ToArray();
  }

  // ── User Story 1 ─────────────────────────────────────────────────────────

  [Fact]
  public async Task ReproductionBodyIsRejectedAndStoresNothing() {
    using var app = CreateFactory();
    var client = await CreateClientWithCommandAsync(app).ConfigureAwait(false);

    var response = await client.PostAsJsonAsync("/api/sequences", ReproductionBody("ZZZ.DropProbe")).ConfigureAwait(false);

    response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    var errors = await ErrorsAsync(response).ConfigureAwait(false);
    errors.Should().Contain("steps[0] (stepId 'a'): each action step must include primitiveAction object.");
    var list = await ListSequencesAsync(client).ConfigureAwait(false);
    list.EnumerateArray().Should().NotContain(s => s.GetProperty("name").GetString() == "ZZZ.DropProbe");
  }

  [Fact]
  public async Task SameStepWithActionStepTypeGetsSameError() {
    using var app = CreateFactory();
    var client = await CreateClientWithCommandAsync(app).ConfigureAwait(false);

    var body = new {
      name = "ZZZ.ActionProbe",
      steps = new object[] {
        new { stepId = "a", stepType = "Action", commandReference = new { commandId = CommandId } },
        RescheduleStep("b")
      }
    };
    var response = await client.PostAsJsonAsync("/api/sequences", body).ConfigureAwait(false);

    response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    var errors = await ErrorsAsync(response).ConfigureAwait(false);
    errors.Should().Contain("steps[0] (stepId 'a'): each action step must include primitiveAction object.");
  }

  [Fact]
  public async Task MalformedSecondStepIsNamedByItsPosition() {
    using var app = CreateFactory();
    var client = await CreateClientWithCommandAsync(app).ConfigureAwait(false);

    var body = new { name = "ZZZ.SecondStep", steps = new[] { RescheduleStep("a"), CommandReferenceOnlyStep("b") } };
    var response = await client.PostAsJsonAsync("/api/sequences", body).ConfigureAwait(false);

    response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    var errors = await ErrorsAsync(response).ConfigureAwait(false);
    errors.Should().Contain(e => e.StartsWith("steps[1] (stepId 'b')", StringComparison.Ordinal));
    var list = await ListSequencesAsync(client).ConfigureAwait(false);
    list.EnumerateArray().Should().NotContain(s => s.GetProperty("name").GetString() == "ZZZ.SecondStep");
  }

  [Fact]
  public async Task UpdateWithMalformedStepIsRejectedAndKeepsStoredSequence() {
    using var app = CreateFactory();
    var client = await CreateClientWithCommandAsync(app).ConfigureAwait(false);

    var create = await client.PostAsJsonAsync("/api/sequences", new {
      name = "ZZZ.UpdateProbe",
      steps = new[] { CommandStep("c1") }
    }).ConfigureAwait(false);
    create.StatusCode.Should().Be(HttpStatusCode.Created);
    var created = await create.Content.ReadFromJsonAsync<JsonElement>().ConfigureAwait(false);
    var id = created.GetProperty("id").GetString()!;
    var version = created.GetProperty("version").GetInt32();

    var update = await client.PutAsJsonAsync($"/api/sequences/{id}", ReproductionBody("ZZZ.UpdateProbe")).ConfigureAwait(false);

    update.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    var errors = await ErrorsAsync(update).ConfigureAwait(false);
    errors.Should().Contain(e => e.StartsWith("steps[0] (stepId 'a')", StringComparison.Ordinal));
    var stored = await client.GetFromJsonAsync<JsonElement>($"/api/sequences/{id}").ConfigureAwait(false);
    stored.GetProperty("version").GetInt32().Should().Be(version);
    stored.GetProperty("steps").GetArrayLength().Should().Be(1);
    stored.GetProperty("steps")[0].GetProperty("stepId").GetString().Should().Be("c1");
  }

  [Fact]
  public async Task StringStepBeforeObjectStepIsRejected() {
    using var app = CreateFactory();
    var client = await CreateClientWithCommandAsync(app).ConfigureAwait(false);

    var body = new { name = "ZZZ.Mixed", steps = new object[] { CommandId, CommandStep("x") } };
    var response = await client.PostAsJsonAsync("/api/sequences", body).ConfigureAwait(false);

    response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    var errors = await ErrorsAsync(response).ConfigureAwait(false);
    errors.Should().Contain("steps[0]: each step must be an object.");
  }

  [Fact]
  public async Task StepWithoutStepIdIsNamedByItsPosition() {
    using var app = CreateFactory();
    var client = await CreateClientWithCommandAsync(app).ConfigureAwait(false);

    var body = new {
      name = "ZZZ.NoStepId",
      steps = new object[] {
        CommandStep("a"),
        new { stepType = "Action", primitiveAction = new { type = "reschedule-self", schemaVersion = "v1", payload = new { option = "AtQueueStart" } } }
      }
    };
    var response = await client.PostAsJsonAsync("/api/sequences", body).ConfigureAwait(false);

    response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    var errors = await ErrorsAsync(response).ConfigureAwait(false);
    errors.Should().Contain("steps[1]: each step must include string stepId.");
  }

  // ── User Story 2 ─────────────────────────────────────────────────────────

  [Fact]
  public async Task DryRunReproductionBodyIsRejectedAndStoresNothing() {
    using var app = CreateFactory();
    var client = await CreateClientWithCommandAsync(app).ConfigureAwait(false);
    var countBefore = (await ListSequencesAsync(client).ConfigureAwait(false)).GetArrayLength();

    var dryRun = await client.PostAsJsonAsync("/api/sequences", ReproductionBody("ZZZ.DropProbe", dryRun: true)).ConfigureAwait(false);
    var real = await client.PostAsJsonAsync("/api/sequences", ReproductionBody("ZZZ.DropProbe")).ConfigureAwait(false);

    dryRun.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    real.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    (await ErrorsAsync(dryRun).ConfigureAwait(false)).Should().Equal(await ErrorsAsync(real).ConfigureAwait(false));
    (await ListSequencesAsync(client).ConfigureAwait(false)).GetArrayLength().Should().Be(countBefore);
  }

  [Fact]
  public async Task DryRunOldShapeBodyIsValidAndStoresNothing() {
    using var app = CreateFactory();
    var client = await CreateClientWithCommandAsync(app).ConfigureAwait(false);
    var countBefore = (await ListSequencesAsync(client).ConfigureAwait(false)).GetArrayLength();

    var response = await client.PostAsJsonAsync("/api/sequences", new { name = "ZZZ.OldDryRun", steps = new[] { CommandId }, dryRun = true }).ConfigureAwait(false);

    response.StatusCode.Should().Be(HttpStatusCode.OK);
    var body = await response.Content.ReadFromJsonAsync<JsonElement>().ConfigureAwait(false);
    body.GetProperty("valid").GetBoolean().Should().BeTrue();
    body.GetProperty("dryRun").GetBoolean().Should().BeTrue();
    body.GetProperty("errors").GetArrayLength().Should().Be(0);
    (await ListSequencesAsync(client).ConfigureAwait(false)).GetArrayLength().Should().Be(countBefore);
  }

  [Fact]
  public async Task OldShapeWithNonStringItemIsRejected() {
    using var app = CreateFactory();
    var client = await CreateClientWithCommandAsync(app).ConfigureAwait(false);

    var response = await client.PostAsJsonAsync("/api/sequences", new { name = "ZZZ.OldNumber", steps = new object[] { CommandId, 5 } }).ConfigureAwait(false);

    response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    var errors = await ErrorsAsync(response).ConfigureAwait(false);
    errors.Should().Contain("steps[1]: each step must be a string command id or a step object.");
  }

  [Fact]
  public async Task OldShapeWithParametersIsRejectedAndNullParametersIsAccepted() {
    using var app = CreateFactory();
    var client = await CreateClientWithCommandAsync(app).ConfigureAwait(false);

    var withParameters = await client.PostAsJsonAsync("/api/sequences", new { name = "ZZZ.OldParams", steps = new[] { CommandId }, parameters = Array.Empty<object>() }).ConfigureAwait(false);
    var withNull = await client.PostAsJsonAsync("/api/sequences", new { name = "ZZZ.OldNullParams", steps = new[] { CommandId }, parameters = (object?)null }).ConfigureAwait(false);

    withParameters.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    (await ErrorsAsync(withParameters).ConfigureAwait(false)).Should().Contain("parameters requires the per-step body shape (steps as step objects).");
    withNull.StatusCode.Should().Be(HttpStatusCode.Created);
  }

  // ── User Story 3 ─────────────────────────────────────────────────────────

  [Fact]
  public async Task ValidPerStepBodyStoresAllStepsAndParameters() {
    using var app = CreateFactory();
    var client = await CreateClientWithCommandAsync(app).ConfigureAwait(false);

    var body = new {
      name = "ZZZ.Valid",
      parameters = new object[] { new { name = "target", type = "text" } },
      steps = new object[] {
        CommandStep("a"),
        new { stepId = "b", stepType = "Action", primitiveAction = new { type = "reschedule-self", schemaVersion = "v1", payload = new { option = "AtQueueStart" } } }
      }
    };
    var response = await client.PostAsJsonAsync("/api/sequences", body).ConfigureAwait(false);

    response.StatusCode.Should().Be(HttpStatusCode.Created);
    var created = await response.Content.ReadFromJsonAsync<JsonElement>().ConfigureAwait(false);
    var id = created.GetProperty("id").GetString()!;
    var stored = await client.GetFromJsonAsync<JsonElement>($"/api/sequences/{id}").ConfigureAwait(false);
    stored.GetProperty("steps").GetArrayLength().Should().Be(2);
    stored.GetProperty("parameters").GetArrayLength().Should().Be(1);
    stored.GetProperty("parameters")[0].GetProperty("name").GetString().Should().Be("target");
  }

  [Fact]
  public async Task OldShapeBodyWithStringIdsIsStillCreated() {
    using var app = CreateFactory();
    var client = await CreateClientWithCommandAsync(app).ConfigureAwait(false);

    var response = await client.PostAsJsonAsync("/api/sequences", new { name = "ZZZ.Old", steps = new[] { CommandId } }).ConfigureAwait(false);

    response.StatusCode.Should().Be(HttpStatusCode.Created);
    var list = await ListSequencesAsync(client).ConfigureAwait(false);
    var item = list.EnumerateArray().Single(s => s.GetProperty("name").GetString() == "ZZZ.Old");
    item.GetProperty("steps").GetArrayLength().Should().Be(1);
  }
}
