using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using GameBot.Domain.Commands;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace GameBot.ContractTests.Sequences;

/// <summary>
/// Contract tests for feature 091 / issue #177 (B-008): a sequence write whose command step names a
/// <c>commandId</c> that resolves to nothing is rejected at write time — with or without
/// <c>dryRun</c> — instead of being stored as an unresolved reference. An unresolved id the stored
/// sequence already references is tolerated on update, so a sequence whose command was deleted can
/// still be re-saved.
/// </summary>
public sealed class SequenceCommandReferenceExistenceContractTests {
  private static WebApplicationFactory<Program> CreateFactory() {
    Environment.SetEnvironmentVariable("GAMEBOT_USE_ADB", "false");
    Environment.SetEnvironmentVariable("GAMEBOT_DYNAMIC_PORT", "true");
    Environment.SetEnvironmentVariable("GAMEBOT_AUTH_TOKEN", "test-token");
    return new WebApplicationFactory<Program>();
  }

  private static HttpClient AuthedClient(WebApplicationFactory<Program> app) {
    var client = app.CreateClient();
    client.DefaultRequestHeaders.Add("Authorization", "Bearer test-token");
    return client;
  }

  // The contract test project shares one data directory across tests, so every id that must NOT
  // exist is unique per test run: a fixed name could be seeded by some other test.
  private static string MissingId() => $"missing-cmd-{Guid.NewGuid():N}";

  private static async Task SeedCommandAsync(WebApplicationFactory<Program> app, string id, string name) {
    var commands = app.Services.GetRequiredService<ICommandRepository>();
    await commands.AddAsync(new Command { Id = id, Name = name }).ConfigureAwait(false);
  }

  private static object TapStep(string stepId) => new {
    stepId,
    label = "Tap",
    stepType = "Action",
    primitiveAction = new { type = "tap", schemaVersion = "v1", payload = new { x = 1, y = 1 } }
  };

  private static object CommandStep(string stepId, string commandId) => new {
    stepId,
    label = "Command",
    stepType = "Action",
    primitiveAction = new { type = "command", schemaVersion = "v1", payload = new { commandId } }
  };

  private static async Task<string> CreateSequenceAsync(HttpClient client, string name, params object[] steps) {
    var response = await client.PostAsJsonAsync("/api/sequences", new { name, version = 1, steps }).ConfigureAwait(false);
    response.StatusCode.Should().Be(HttpStatusCode.Created);
    var created = await response.Content.ReadFromJsonAsync<JsonElement>().ConfigureAwait(false);
    return created.GetProperty("id").GetString()!;
  }

  private static async Task<JsonElement> GetSequenceAsync(HttpClient client, string sequenceId) {
    var response = await client.GetAsync(new Uri($"/api/sequences/{sequenceId}", UriKind.Relative)).ConfigureAwait(false);
    response.StatusCode.Should().Be(HttpStatusCode.OK);
    return await response.Content.ReadFromJsonAsync<JsonElement>().ConfigureAwait(false);
  }

  private static async Task<string[]> ReadErrorsAsync(HttpResponseMessage response) {
    response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    var body = await response.Content.ReadFromJsonAsync<JsonElement>().ConfigureAwait(false);
    return body.GetProperty("errors").EnumerateArray().Select(e => e.GetString()!).ToArray();
  }

  private static async Task<bool> SequenceNamedExistsAsync(HttpClient client, string name) {
    var list = await client.GetFromJsonAsync<JsonElement>(new Uri("/api/sequences", UriKind.Relative)).ConfigureAwait(false);
    return list.EnumerateArray().Any(s => s.GetProperty("name").GetString() == name);
  }

  [Fact]
  public async Task CreateWithNonexistentCommandIdIsRejectedAndNothingStored() {
    using var app = CreateFactory();
    var client = AuthedClient(app);
    var missing = MissingId();
    var name = $"create-missing-command-{Guid.NewGuid():N}";

    var response = await client.PostAsJsonAsync("/api/sequences", new {
      name,
      version = 1,
      steps = new[] { CommandStep("step-1", missing) }
    }).ConfigureAwait(false);

    var errors = await ReadErrorsAsync(response).ConfigureAwait(false);
    errors.Should().Contain(e => e.Contains(missing) && e.Contains("step-1"));
    (await SequenceNamedExistsAsync(client, name).ConfigureAwait(false)).Should().BeFalse();
  }

  [Fact]
  public async Task PutWithDryRunAndNonexistentCommandIdIsRejectedAndNothingChanges() {
    using var app = CreateFactory();
    var client = AuthedClient(app);
    var missing = MissingId();
    var sequenceId = await CreateSequenceAsync(client, "put-dry-run-missing-command", TapStep("tap-original")).ConfigureAwait(false);

    var response = await client.PutAsJsonAsync($"/api/sequences/{sequenceId}", new {
      name = "put-dry-run-missing-command",
      version = 1,
      dryRun = true,
      steps = new[] { CommandStep("step-1", missing) }
    }).ConfigureAwait(false);

    var errors = await ReadErrorsAsync(response).ConfigureAwait(false);
    errors.Should().Contain(e => e.Contains(missing) && e.Contains("step-1"));

    var fetched = await GetSequenceAsync(client, sequenceId).ConfigureAwait(false);
    fetched.GetProperty("version").GetInt32().Should().Be(1);
    fetched.GetProperty("steps")[0].GetProperty("stepId").GetString().Should().Be("tap-original");
  }

  [Theory]
  [InlineData("PUT")]
  [InlineData("PATCH")]
  public async Task UpdateWithNonexistentCommandIdIsRejectedAndNothingChanges(string method) {
    using var app = CreateFactory();
    var client = AuthedClient(app);
    var missing = MissingId();
    var sequenceId = await CreateSequenceAsync(client, "update-missing-command", TapStep("tap-original")).ConfigureAwait(false);

    var body = new { name = "update-missing-command", version = 1, steps = new[] { CommandStep("step-1", missing) } };
    var response = method == "PUT"
      ? await client.PutAsJsonAsync($"/api/sequences/{sequenceId}", body).ConfigureAwait(false)
      : await client.PatchAsJsonAsync($"/api/sequences/{sequenceId}", body).ConfigureAwait(false);

    var errors = await ReadErrorsAsync(response).ConfigureAwait(false);
    errors.Should().Contain(e => e.Contains(missing) && e.Contains("step-1"));

    var fetched = await GetSequenceAsync(client, sequenceId).ConfigureAwait(false);
    fetched.GetProperty("version").GetInt32().Should().Be(1);
    fetched.GetProperty("steps")[0].GetProperty("stepId").GetString().Should().Be("tap-original");
  }

  [Theory]
  [InlineData("loop")]
  [InlineData("if")]
  [InlineData("else")]
  public async Task NonexistentCommandIdNestedInABodyIsRejected(string position) {
    using var app = CreateFactory();
    var client = AuthedClient(app);
    var missing = MissingId();
    var nested = CommandStep("nested-step", missing);

    object container = position switch {
      "loop" => new {
        stepId = "loop-1",
        stepType = "Loop",
        loop = new { loopType = "count", count = 1, maxIterations = 1 },
        body = new[] { nested }
      },
      "if" => new {
        stepId = "if-1",
        stepType = "If",
        @if = new { condition = new { type = "commandOutcome", stepRef = "tap-first", expectedState = "success" } },
        body = new[] { nested }
      },
      _ => new {
        stepId = "if-1",
        stepType = "If",
        @if = new { condition = new { type = "commandOutcome", stepRef = "tap-first", expectedState = "success" } },
        body = new[] { TapStep("tap-then") },
        elseBody = new[] { nested }
      }
    };

    var response = await client.PostAsJsonAsync("/api/sequences", new {
      name = $"nested-missing-command-{position}",
      version = 1,
      steps = new[] { TapStep("tap-first"), container }
    }).ConfigureAwait(false);

    var errors = await ReadErrorsAsync(response).ConfigureAwait(false);
    errors.Should().Contain(e => e.Contains(missing) && e.Contains("nested-step"));
  }

  [Fact]
  public async Task SameMissingIdUsedByTwoStepsProducesOneErrorListingBoth() {
    using var app = CreateFactory();
    var client = AuthedClient(app);
    var missing = MissingId();

    var response = await client.PostAsJsonAsync("/api/sequences", new {
      name = "missing-command-used-twice",
      version = 1,
      steps = new[] { CommandStep("step-1", missing), CommandStep("step-2", missing) }
    }).ConfigureAwait(false);

    var errors = await ReadErrorsAsync(response).ConfigureAwait(false);
    var matching = errors.Where(e => e.Contains(missing, StringComparison.Ordinal)).ToArray();
    matching.Should().ContainSingle();
    matching[0].Should().Contain("step-1").And.Contain("step-2");
  }

  [Fact]
  public async Task CommandStepWithExistingCommandIsAccepted() {
    using var app = CreateFactory();
    var client = AuthedClient(app);
    var commandId = $"existing-cmd-{Guid.NewGuid():N}";
    await SeedCommandAsync(app, commandId, "Existing Command").ConfigureAwait(false);

    var sequenceId = await CreateSequenceAsync(client, "existing-command-accepted", CommandStep("step-1", commandId)).ConfigureAwait(false);

    var put = await client.PutAsJsonAsync($"/api/sequences/{sequenceId}", new {
      name = "existing-command-accepted",
      version = 1,
      steps = new[] { CommandStep("step-1", commandId), TapStep("tap-1") }
    }).ConfigureAwait(false);
    put.StatusCode.Should().Be(HttpStatusCode.OK);
  }

  [Fact]
  public async Task CommandIdDifferingOnlyInCaseFromAnExistingCommandIsAccepted() {
    using var app = CreateFactory();
    var client = AuthedClient(app);
    var suffix = Guid.NewGuid().ToString("N");
    await SeedCommandAsync(app, $"Cmd-Upper-{suffix}", "Upper Command").ConfigureAwait(false);

    var response = await client.PostAsJsonAsync("/api/sequences", new {
      name = "case-insensitive-command",
      version = 1,
      steps = new[] { CommandStep("step-1", $"cmd-upper-{suffix}") }
    }).ConfigureAwait(false);

    response.StatusCode.Should().Be(HttpStatusCode.Created);
  }

  [Fact]
  public async Task PutCarryingAlreadyStoredUnresolvedCommandIdIsAccepted() {
    using var app = CreateFactory();
    var client = AuthedClient(app);
    var commandId = $"deleted-cmd-{Guid.NewGuid():N}";
    await SeedCommandAsync(app, commandId, "Soon Deleted").ConfigureAwait(false);
    var sequenceId = await CreateSequenceAsync(client, "carry-over-unresolved", CommandStep("step-1", commandId)).ConfigureAwait(false);
    await app.Services.GetRequiredService<ICommandRepository>().DeleteAsync(commandId).ConfigureAwait(false);

    var put = await client.PutAsJsonAsync($"/api/sequences/{sequenceId}", new {
      name = "carry-over-unresolved",
      version = 1,
      steps = new[] { CommandStep("step-1", commandId) }
    }).ConfigureAwait(false);
    put.StatusCode.Should().Be(HttpStatusCode.OK);

    var reference = (await GetSequenceAsync(client, sequenceId).ConfigureAwait(false))
      .GetProperty("steps")[0].GetProperty("commandReference");
    reference.GetProperty("isResolved").GetBoolean().Should().BeFalse();
    reference.GetProperty("commandName").GetString().Should().Be("Soon Deleted");
  }

  [Fact]
  public async Task PutIntroducingDifferentNonexistentCommandIdIsRejectedEvenWhenAnotherIsTolerated() {
    using var app = CreateFactory();
    var client = AuthedClient(app);
    var commandId = $"deleted-cmd-{Guid.NewGuid():N}";
    var missing = MissingId();
    await SeedCommandAsync(app, commandId, "Soon Deleted").ConfigureAwait(false);
    var sequenceId = await CreateSequenceAsync(client, "carry-over-plus-new-missing", CommandStep("step-1", commandId)).ConfigureAwait(false);
    await app.Services.GetRequiredService<ICommandRepository>().DeleteAsync(commandId).ConfigureAwait(false);

    var put = await client.PutAsJsonAsync($"/api/sequences/{sequenceId}", new {
      name = "carry-over-plus-new-missing",
      version = 1,
      steps = new[] { CommandStep("step-1", commandId), CommandStep("step-2", missing) }
    }).ConfigureAwait(false);

    var errors = await ReadErrorsAsync(put).ConfigureAwait(false);
    errors.Should().Contain(e => e.Contains(missing) && e.Contains("step-2"));
    errors.Should().NotContain(e => e.Contains(commandId));
    (await GetSequenceAsync(client, sequenceId).ConfigureAwait(false)).GetProperty("version").GetInt32().Should().Be(1);
  }
}
