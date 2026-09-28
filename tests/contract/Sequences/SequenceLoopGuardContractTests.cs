using System;
using System.Linq;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace GameBot.ContractTests.Sequences;

/// <summary>
/// Issue #232 (spec 110): a condition on a top-level Loop step is a guard. The save endpoints keep
/// it, return it on read, and check it with the same rules as the condition of an Action step.
/// Before the fix, the save mapping dropped the condition of a Loop step, so the guard never ran.
/// </summary>
public sealed class SequenceLoopGuardContractTests : IDisposable {
  private readonly string? _prevToken;
  private readonly string? _prevAdb;

  public SequenceLoopGuardContractTests() {
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

  private static System.Net.Http.HttpClient CreateClient(WebApplicationFactory<Program> app) {
    var client = app.CreateClient();
    client.DefaultRequestHeaders.Add("Authorization", "Bearer test-token");
    return client;
  }

  private static object GuardedLoopSequence(string name, object guard, bool? dryRun = null) => new {
    name,
    version = 1,
    dryRun,
    steps = new object[] {
      new {
        stepId = "first",
        primitiveAction = new { type = "tap", schemaVersion = "v1", payload = new { x = 10, y = 10 } }
      },
      new {
        stepId = "leave-if-stuck",
        stepType = "Loop",
        condition = guard,
        loop = new { loopType = "count", count = 2 },
        body = new object[] {
          new {
            stepId = "inner",
            primitiveAction = new { type = "tap", schemaVersion = "v1", payload = new { x = 20, y = 20 } }
          }
        }
      }
    }
  };

  private static object NoneLastRunGuard() => new {
    type = "none",
    children = new object[] {
      new { type = "lastRun", sequence = "self", status = "success", within = "01:00:00" }
    }
  };

  [Fact]
  public async Task LoopGuardSurvivesSaveAndRead() {
    using var app = new WebApplicationFactory<Program>();
    using var client = CreateClient(app);

    var created = await client.PostAsJsonAsync(
      "/api/sequences", GuardedLoopSequence($"loop-guard-{Guid.NewGuid():N}", NoneLastRunGuard())).ConfigureAwait(false);
    var createdBody = await created.Content.ReadAsStringAsync().ConfigureAwait(false);
    created.StatusCode.Should().Be(HttpStatusCode.Created, createdBody);
    var sequenceId = JsonDocument.Parse(createdBody).RootElement.GetProperty("id").GetString();

    var read = await client.GetAsync(new Uri($"/api/sequences/{sequenceId}", UriKind.Relative)).ConfigureAwait(false);
    var readBody = await read.Content.ReadAsStringAsync().ConfigureAwait(false);
    read.StatusCode.Should().Be(HttpStatusCode.OK, readBody);

    using var doc = JsonDocument.Parse(readBody);
    var loop = doc.RootElement.GetProperty("steps").EnumerateArray()
      .Single(step => step.GetProperty("stepId").GetString() == "leave-if-stuck");
    loop.TryGetProperty("condition", out var condition).Should().BeTrue(readBody);
    condition.ValueKind.Should().Be(JsonValueKind.Object, readBody);
    condition.GetProperty("type").GetString().Should().Be("none");
    var children = condition.GetProperty("children").EnumerateArray().ToArray();
    children.Should().ContainSingle();
    children[0].GetProperty("type").GetString().Should().Be("lastRun");
  }

  [Fact]
  public async Task DryRunRejectsLoopGuardWithUnknownReference() {
    using var app = new WebApplicationFactory<Program>();
    using var client = CreateClient(app);

    var guard = new { type = "commandOutcome", stepRef = "nope", expectedState = "success" };
    var response = await client.PostAsJsonAsync(
      "/api/sequences", GuardedLoopSequence($"loop-guard-dry-{Guid.NewGuid():N}", guard, dryRun: true)).ConfigureAwait(false);
    var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

    response.StatusCode.Should().Be(HttpStatusCode.BadRequest, body);
    body.Should().Contain("leave-if-stuck").And.Contain("nope");
  }

  [Fact]
  public async Task CreateRejectsLoopGuardImageVisibleWithoutImageId() {
    using var app = new WebApplicationFactory<Program>();
    using var client = CreateClient(app);

    var guard = new { type = "imageVisible", imageId = "" };
    var response = await client.PostAsJsonAsync(
      "/api/sequences", GuardedLoopSequence($"loop-guard-img-{Guid.NewGuid():N}", guard)).ConfigureAwait(false);
    var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

    response.StatusCode.Should().Be(HttpStatusCode.BadRequest, body);
    body.Should().Contain("leave-if-stuck");
  }

  [Theory]
  [InlineData("SequenceStep")]
  [InlineData("SequenceStepContract")]
  public async Task OpenApiStepSchemaDescribesLoopGuard(string schema) {
    using var app = new WebApplicationFactory<Program>();
    var client = app.CreateClient();

    var response = await client.GetAsync(new Uri("/swagger/v1/swagger.json", UriKind.Relative)).ConfigureAwait(false);
    response.EnsureSuccessStatusCode();
    using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync().ConfigureAwait(false));
    var step = document.RootElement.GetProperty("components").GetProperty("schemas").GetProperty(schema);

    step.GetProperty("description").GetString().Should()
      .Contain("On a Loop step, the condition is evaluated one time before the first iteration");
  }
}
