using System;
using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace GameBot.ContractTests.Sequences;

public sealed class SequenceActionPayloadValidationContractTests {
  private static WebApplicationFactory<Program> CreateFactory() {
    Environment.SetEnvironmentVariable("GAMEBOT_USE_ADB", "false");
    Environment.SetEnvironmentVariable("GAMEBOT_DYNAMIC_PORT", "true");
    Environment.SetEnvironmentVariable("GAMEBOT_AUTH_TOKEN", "test-token");
    return new WebApplicationFactory<Program>();
  }

  [Fact]
  public async Task CreateSequenceRejectsUnsupportedActionPayloadType() {
    using var app = CreateFactory();
    var client = app.CreateClient();
    client.DefaultRequestHeaders.Add("Authorization", "Bearer test-token");

    var payload = new {
      name = "unsupported-action-payload",
      version = 1,
      steps = new object[] {
        new {
          stepId = "action",
          label = "Action",
          action = new {
            type = "unsupported",
            parameters = new { foo = 1 }
          }
        }
      }
    };

    var response = await client.PostAsJsonAsync("/api/sequences", payload).ConfigureAwait(false);
    response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
    var content = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
    content.Should().MatchRegex("(?i)unsupported action type");
  }

  [Fact]
  public async Task CreateSequenceRejectsMalformedActionPayloadReference() {
    using var app = CreateFactory();
    var client = app.CreateClient();
    client.DefaultRequestHeaders.Add("Authorization", "Bearer test-token");

    var payload = new {
      name = "malformed-action-payload",
      version = 1,
      steps = new object[] {
        new {
          stepId = "action",
          label = "Action",
          action = new {
            type = string.Empty,
            parameters = new { x = 1 }
          }
        }
      }
    };

    var response = await client.PostAsJsonAsync("/api/sequences", payload).ConfigureAwait(false);
    response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    var content = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
    content.Should().MatchRegex("(?i)action type");
  }
}
