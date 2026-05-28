using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace GameBot.IntegrationTests.Sequences;

[Collection("ConfigIsolation")]
public sealed class WaitForImageSequenceAuthoringIntegrationTests : IDisposable {
  private readonly string? _prevUseAdb;
  private readonly string? _prevDynamicPort;
  private readonly string? _prevAuthToken;
  private readonly string? _prevDataDir;

  public WaitForImageSequenceAuthoringIntegrationTests() {
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

  [Fact]
  public async Task SequenceCreateReadUpdateSupportsWaitForImageRoundTrip() {
    using var app = new WebApplicationFactory<Program>();
    var client = app.CreateClient();
    client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "test-token");

    var createPayload = new {
      name = "wait-authoring-sequence",
      version = 1,
      steps = new object[] {
        new {
          stepId = "step-1",
          primitiveAction = new {
            type = "WaitForImage",
            schemaVersion = "v1",
            payload = new {
              detectionTarget = new {
                referenceImageId = "home_button",
                confidence = 0.8
              },
              timeoutMs = 1500
            }
          }
        }
      }
    };

    var createResponse = await client.PostAsJsonAsync(new Uri("/api/sequences", UriKind.Relative), createPayload).ConfigureAwait(false);
    createResponse.StatusCode.Should().Be(HttpStatusCode.Created);

    var created = await createResponse.Content.ReadFromJsonAsync<JsonElement>().ConfigureAwait(false);
    var sequenceId = created.GetProperty("id").GetString();
    sequenceId.Should().NotBeNullOrWhiteSpace();
    created.GetProperty("steps")[0].GetProperty("primitiveAction").GetProperty("type").GetString().Should().Be("WaitForImage");

    var getResponse = await client.GetAsync(new Uri($"/api/sequences/{sequenceId}", UriKind.Relative)).ConfigureAwait(false);
    getResponse.StatusCode.Should().Be(HttpStatusCode.OK);
    var fetched = await getResponse.Content.ReadFromJsonAsync<JsonElement>().ConfigureAwait(false);
    fetched.GetProperty("steps")[0].GetProperty("primitiveAction").GetProperty("payload").GetProperty("detectionTarget").GetProperty("referenceImageId").GetString().Should().Be("home_button");

    var patchPayload = new {
      name = "wait-authoring-sequence-updated",
      version = 1,
      steps = new object[] {
        new {
          stepId = "step-1",
          primitiveAction = new {
            type = "WaitForImage",
            schemaVersion = "v1",
            payload = new {
              timeoutMs = 2200
            }
          }
        }
      }
    };

    var patchResponse = await client.PutAsJsonAsync(new Uri($"/api/sequences/{sequenceId}", UriKind.Relative), patchPayload).ConfigureAwait(false);
    patchResponse.StatusCode.Should().Be(HttpStatusCode.OK);

    var updatedResponse = await client.GetAsync(new Uri($"/api/sequences/{sequenceId}", UriKind.Relative)).ConfigureAwait(false);
    updatedResponse.StatusCode.Should().Be(HttpStatusCode.OK);
    var updated = await updatedResponse.Content.ReadFromJsonAsync<JsonElement>().ConfigureAwait(false);

    updated.GetProperty("name").GetString().Should().Be("wait-authoring-sequence-updated");
    var primitiveAction = updated.GetProperty("steps")[0].GetProperty("primitiveAction");
    primitiveAction.GetProperty("type").GetString().Should().Be("WaitForImage");
    primitiveAction.GetProperty("payload").GetProperty("timeoutMs").GetInt32().Should().Be(2200);
    primitiveAction.GetProperty("payload").TryGetProperty("detectionTarget", out _).Should().BeFalse();
  }

  [Fact]
  public async Task CreateSequenceAcceptsZeroTimeoutForWaitForImage() {
    using var app = new WebApplicationFactory<Program>();
    var client = app.CreateClient();
    client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "test-token");

    var sequenceReq = new {
      name = "ZeroTimeoutWaitSequence",
      version = 1,
      steps = new object[] {
        new {
          stepId = "step-1",
          primitiveAction = new {
            type = "WaitForImage",
            schemaVersion = "v1",
            payload = new {
              timeoutMs = 0
            }
          }
        }
      }
    };

    var response = await client.PostAsJsonAsync(new Uri("/api/sequences", UriKind.Relative), sequenceReq).ConfigureAwait(false);
    response.StatusCode.Should().Be(HttpStatusCode.Created);

    var payload = await response.Content.ReadFromJsonAsync<JsonElement>().ConfigureAwait(false);
    payload.GetProperty("steps")[0].GetProperty("primitiveAction").GetProperty("payload").GetProperty("timeoutMs").GetInt32().Should().Be(0);
  }

  [Fact]
  public async Task CreateSequenceRejectsNegativeTimeoutForWaitForImage() {
    using var app = new WebApplicationFactory<Program>();
    var client = app.CreateClient();
    client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "test-token");

    var sequenceReq = new {
      name = "NegativeTimeoutWaitSequence",
      version = 1,
      steps = new object[] {
        new {
          stepId = "step-1",
          primitiveAction = new {
            type = "WaitForImage",
            schemaVersion = "v1",
            payload = new {
              timeoutMs = -1
            }
          }
        }
      }
    };

    var response = await client.PostAsJsonAsync(new Uri("/api/sequences", UriKind.Relative), sequenceReq).ConfigureAwait(false);
    response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

    var payload = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
    payload.Should().Contain("waitForImage timeoutMs must be greater than or equal to zero");
  }
}