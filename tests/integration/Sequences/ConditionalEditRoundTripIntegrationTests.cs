using System;
using System.Linq;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace GameBot.IntegrationTests.Sequences;

public sealed class ConditionalEditRoundTripIntegrationTests {
  private const string OneByOnePngBase64 = "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO2n5u4AAAAASUVORK5CYII=";

  [Fact]
  public async Task PatchConditionalFlowPersistsEditedConditionAfterReload() {
    TestEnvironment.PrepareCleanDataDir();
    Environment.SetEnvironmentVariable("GAMEBOT_AUTH_TOKEN", "test-token");

    using var app = new WebApplicationFactory<Program>();
    var client = app.CreateClient();
    client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "test-token");

    var uploadA = await client.PostAsJsonAsync(new Uri("/api/images", UriKind.Relative), new { id = "image-a", data = OneByOnePngBase64 }).ConfigureAwait(false);
    uploadA.StatusCode.Should().Be(HttpStatusCode.Created);
    var uploadB = await client.PostAsJsonAsync(new Uri("/api/images", UriKind.Relative), new { id = "image-b", data = OneByOnePngBase64 }).ConfigureAwait(false);
    uploadB.StatusCode.Should().Be(HttpStatusCode.Created);

    var createPayload = new {
      name = "conditional-edit-sequence",
      version = 1,
      steps = new object[] {
        new {
          stepId = "step-1",
          label = "Step One",
          action = new {
            type = "tap",
            parameters = new { x = 120, y = 840 }
          }
        },
        new {
          stepId = "step-2",
          label = "Step Two",
          action = new {
            type = "tap",
            parameters = new { x = 220, y = 940 }
          },
          condition = new {
            type = "imageVisible",
            imageId = "image-a",
            minSimilarity = 0.80
          }
        }
      }
    };

    var createResponse = await client.PostAsJsonAsync("/api/sequences", createPayload).ConfigureAwait(false);
    createResponse.StatusCode.Should().Be(HttpStatusCode.Created);
    var created = await createResponse.Content.ReadFromJsonAsync<JsonElement>().ConfigureAwait(false);
    var sequenceId = created.GetProperty("id").GetString();
    sequenceId.Should().NotBeNullOrWhiteSpace();

    var patchPayload = new {
      name = "conditional-edit-sequence",
      version = 1,
      steps = new object[] {
        new {
          stepId = "step-1",
          label = "Step One",
          action = new {
            type = "tap",
            parameters = new { x = 120, y = 840 }
          }
        },
        new {
          stepId = "step-2",
          label = "Step Two",
          action = new {
            type = "tap",
            parameters = new { x = 220, y = 940 }
          },
          condition = new {
            type = "imageVisible",
            imageId = "image-b",
            minSimilarity = 0.95
          }
        }
      }
    };

    var patchResponse = await client.PatchAsJsonAsync($"/api/sequences/{sequenceId}", patchPayload).ConfigureAwait(false);
    patchResponse.StatusCode.Should().Be(HttpStatusCode.OK);

    var getResponse = await client.GetAsync(new Uri($"/api/sequences/{sequenceId}", UriKind.Relative)).ConfigureAwait(false);
    getResponse.StatusCode.Should().Be(HttpStatusCode.OK);
    var fetched = await getResponse.Content.ReadFromJsonAsync<JsonElement>().ConfigureAwait(false);

    var conditionStep = fetched.GetProperty("steps").EnumerateArray().First(step => step.GetProperty("stepId").GetString() == "step-2");
    var condition = conditionStep.GetProperty("condition");
    condition.GetProperty("type").GetString().Should().Be("imageVisible");
    condition.GetProperty("imageId").GetString().Should().Be("image-b");
    condition.GetProperty("minSimilarity").GetDouble().Should().Be(0.95);
  }
}
