using System;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using GameBot.Domain.Commands;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace GameBot.IntegrationTests.Sequences;

/// <summary>
/// Integration tests for bug B-003: POST/PUT /api/sequences must reject a "command" step whose
/// payload names a command by commandName (or supplies neither) instead of a resolved commandId,
/// at creation/replace time — not silently accept it and fail confusingly at run time.
/// </summary>
public sealed class SequenceCommandIdValidationIntegrationTests {
  private static WebApplicationFactory<Program> CreateFactory() {
    Environment.SetEnvironmentVariable("GAMEBOT_USE_ADB", "false");
    Environment.SetEnvironmentVariable("GAMEBOT_DYNAMIC_PORT", "true");
    Environment.SetEnvironmentVariable("GAMEBOT_AUTH_TOKEN", "test-token");
    TestEnvironment.PrepareCleanDataDir();
    return new WebApplicationFactory<Program>();
  }

  private static object MalformedTopLevelPayload(string name) => new {
    name,
    version = 1,
    steps = new object[] {
      new {
        stepId = "step-1",
        label = "Bad command reference",
        stepType = "Action",
        primitiveAction = new {
          type = "command",
          schemaVersion = "v1",
          payload = new { commandName = "some-command" }
        }
      }
    }
  };

  private static object MalformedNestedInLoopPayload(string name) => new {
    name,
    version = 1,
    steps = new object[] {
      new {
        stepId = "loop-1",
        label = "Loop",
        stepType = "Loop",
        loop = new { loopType = "count", count = 1, maxIterations = 1 },
        body = new object[] {
          new {
            stepId = "step-1",
            label = "Bad command reference",
            stepType = "Action",
            primitiveAction = new {
              type = "command",
              schemaVersion = "v1",
              payload = new { commandName = "some-command" }
            }
          }
        }
      }
    }
  };

  [Fact]
  public async Task CreateSequenceWithCommandNameButNoCommandIdIsRejected() {
    using var app = CreateFactory();
    var client = app.CreateClient();
    client.DefaultRequestHeaders.Add("Authorization", "Bearer test-token");

    var response = await client.PostAsJsonAsync("/api/sequences", MalformedTopLevelPayload("bad-command-ref")).ConfigureAwait(false);

    response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    var body = await response.Content.ReadFromJsonAsync<JsonElement>().ConfigureAwait(false);
    body.GetProperty("errors").EnumerateArray().Should().Contain(e => e.GetString()!.Contains("step-1") && e.GetString()!.Contains("commandId"));

    var listResponse = await client.GetAsync(new Uri("/api/sequences", UriKind.Relative)).ConfigureAwait(false);
    listResponse.StatusCode.Should().Be(HttpStatusCode.OK);
    var list = await listResponse.Content.ReadFromJsonAsync<JsonElement>().ConfigureAwait(false);
    list.EnumerateArray().Should().NotContain(s => s.GetProperty("name").GetString() == "bad-command-ref");
  }

  [Fact]
  public async Task CreateSequenceWithCommandNameNestedInLoopBodyIsRejected() {
    using var app = CreateFactory();
    var client = app.CreateClient();
    client.DefaultRequestHeaders.Add("Authorization", "Bearer test-token");

    var response = await client.PostAsJsonAsync("/api/sequences", MalformedNestedInLoopPayload("bad-nested-command-ref")).ConfigureAwait(false);

    response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    var body = await response.Content.ReadFromJsonAsync<JsonElement>().ConfigureAwait(false);
    body.GetProperty("errors").EnumerateArray().Should().Contain(e => e.GetString()!.Contains("step-1") && e.GetString()!.Contains("commandId"));
  }

  [Fact]
  public async Task ReplaceSequenceWithCommandNameButNoCommandIdIsRejected() {
    using var app = CreateFactory();
    var client = app.CreateClient();
    client.DefaultRequestHeaders.Add("Authorization", "Bearer test-token");

    var validPayload = new {
      name = "will-be-replaced",
      version = 1,
      steps = new object[] {
        new {
          stepId = "tap-1",
          label = "Tap",
          stepType = "Action",
          primitiveAction = new { type = "tap", schemaVersion = "v1", payload = new { x = 1, y = 1 } }
        }
      }
    };
    var createResponse = await client.PostAsJsonAsync("/api/sequences", validPayload).ConfigureAwait(false);
    createResponse.StatusCode.Should().Be(HttpStatusCode.Created);
    var created = await createResponse.Content.ReadFromJsonAsync<JsonElement>().ConfigureAwait(false);
    var sequenceId = created.GetProperty("id").GetString();

    var replacePayload = new {
      name = "will-be-replaced",
      version = 1,
      steps = new object[] {
        new {
          stepId = "step-1",
          label = "Bad command reference",
          stepType = "Action",
          primitiveAction = new {
            type = "command",
            schemaVersion = "v1",
            payload = new { commandName = "some-command" }
          }
        }
      }
    };
    var putResponse = await client.PutAsJsonAsync($"/api/sequences/{sequenceId}", replacePayload).ConfigureAwait(false);

    putResponse.StatusCode.Should().Be(HttpStatusCode.BadRequest);

    var getResponse = await client.GetAsync(new Uri($"/api/sequences/{sequenceId}", UriKind.Relative)).ConfigureAwait(false);
    var stillThere = await getResponse.Content.ReadFromJsonAsync<JsonElement>().ConfigureAwait(false);
    stillThere.GetProperty("steps")[0].GetProperty("primitiveAction").GetProperty("type").GetString().Should().Be("tap");
  }

  [Fact]
  public async Task CreateSequenceWithValidCommandIdIsAccepted() {
    using var app = CreateFactory();
    var client = app.CreateClient();
    client.DefaultRequestHeaders.Add("Authorization", "Bearer test-token");

    var commandRepository = app.Services.GetRequiredService<ICommandRepository>();
    await commandRepository.AddAsync(new Command { Id = "real-cmd", Name = "Real Command" }).ConfigureAwait(false);

    var payload = new {
      name = "good-command-ref",
      version = 1,
      steps = new object[] {
        new {
          stepId = "step-1",
          label = "Good command reference",
          stepType = "Action",
          primitiveAction = new {
            type = "command",
            schemaVersion = "v1",
            payload = new { commandId = "real-cmd" }
          }
        }
      }
    };

    var response = await client.PostAsJsonAsync("/api/sequences", payload).ConfigureAwait(false);

    response.StatusCode.Should().Be(HttpStatusCode.Created);
  }
}
