using System;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace GameBot.ContractTests.Sequences;

/// <summary>
/// Feature 065: the <c>reschedule-self</c> action round-trips through the sequence authoring API
/// (FR-001a) including inside an IF/conditional branch (FR-002), and malformed option/timer
/// combinations are rejected (validation contract).
/// </summary>
public sealed class SelfRescheduleActionContractTests {
  private static WebApplicationFactory<Program> CreateFactory() {
    Environment.SetEnvironmentVariable("GAMEBOT_USE_ADB", "false");
    Environment.SetEnvironmentVariable("GAMEBOT_DYNAMIC_PORT", "true");
    Environment.SetEnvironmentVariable("GAMEBOT_AUTH_TOKEN", "test-token");
    return new WebApplicationFactory<Program>();
  }

  private static System.Net.Http.HttpClient AuthedClient(WebApplicationFactory<Program> app) {
    var client = app.CreateClient();
    client.DefaultRequestHeaders.Add("Authorization", "Bearer test-token");
    return client;
  }

  [Fact] // T020 — round-trips unchanged, including under an IF (commandOutcome) condition.
  public async Task RescheduleSelfActionRoundTripsIncludingUnderACondition() {
    using var app = CreateFactory();
    var client = AuthedClient(app);

    var createPayload = new {
      name = "self-reschedule-contract",
      version = 1,
      steps = new object[] {
        new {
          stepId = "go-home",
          label = "Go Home",
          primitiveAction = new { type = "tap", schemaVersion = "v1", payload = new { x = 10, y = 20 } }
        },
        new {
          stepId = "reschedule",
          label = "Reschedule self",
          stepType = "Action",
          primitiveAction = new {
            type = "reschedule-self",
            schemaVersion = "1",
            payload = new { option = "OncePerRun" }
          },
          // Placed under an IF: only fires when the prior step succeeded (FR-002).
          condition = new { type = "commandOutcome", stepRef = "go-home", expectedState = "success" }
        }
      }
    };

    var createResponse = await client.PostAsJsonAsync("/api/sequences", createPayload).ConfigureAwait(false);
    createResponse.StatusCode.Should().Be(HttpStatusCode.Created);
    var created = await createResponse.Content.ReadFromJsonAsync<JsonElement>().ConfigureAwait(false);
    var sequenceId = created.GetProperty("id").GetString();

    var getResponse = await client.GetAsync(new Uri($"/api/sequences/{sequenceId}", UriKind.Relative)).ConfigureAwait(false);
    var fetched = await getResponse.Content.ReadFromJsonAsync<JsonElement>().ConfigureAwait(false);
    var rescheduleStep = fetched.GetProperty("steps")[1];
    rescheduleStep.GetProperty("primitiveAction").GetProperty("type").GetString().Should().Be("reschedule-self");
    rescheduleStep.GetProperty("primitiveAction").GetProperty("payload").GetProperty("option").GetString().Should().Be("OncePerRun");
    rescheduleStep.GetProperty("condition").GetProperty("type").GetString().Should().Be("commandOutcome");
  }

  [Fact] // T035 (US2) — Timer with a relative offset round-trips with the single timer field.
  public async Task TimerRescheduleActionRoundTripsWithRelativeOffset() {
    using var app = CreateFactory();
    var client = AuthedClient(app);

    var createPayload = new {
      name = "self-reschedule-timer-contract",
      version = 1,
      steps = new object[] {
        new {
          stepId = "reschedule",
          stepType = "Action",
          primitiveAction = new {
            type = "reschedule-self",
            schemaVersion = "1",
            payload = new { option = "Timer", timerRelativeOffset = "00:10:00" }
          }
        }
      }
    };

    var createResponse = await client.PostAsJsonAsync("/api/sequences", createPayload).ConfigureAwait(false);
    createResponse.StatusCode.Should().Be(HttpStatusCode.Created);
    var created = await createResponse.Content.ReadFromJsonAsync<JsonElement>().ConfigureAwait(false);
    var sequenceId = created.GetProperty("id").GetString();

    var getResponse = await client.GetAsync(new Uri($"/api/sequences/{sequenceId}", UriKind.Relative)).ConfigureAwait(false);
    var fetched = await getResponse.Content.ReadFromJsonAsync<JsonElement>().ConfigureAwait(false);
    var payload = fetched.GetProperty("steps")[0].GetProperty("primitiveAction").GetProperty("payload");
    payload.GetProperty("option").GetString().Should().Be("Timer");
    payload.GetProperty("timerRelativeOffset").GetString().Should().Be("00:10:00");
  }

  // ── feature 068: ocrOffset round-trip + validation ────────────────────────

  [Fact]
  public async Task TimerRescheduleWithOcrOffsetRoundTrips() {
    using var app = CreateFactory();
    var client = AuthedClient(app);

    var createPayload = new {
      name = "self-reschedule-ocr-contract",
      version = 1,
      steps = new object[] {
        new {
          stepId = "reschedule",
          stepType = "Action",
          primitiveAction = new {
            type = "reschedule-self",
            schemaVersion = "1",
            payload = new {
              option = "Timer",
              ocrOffset = new {
                region = new { x = 10, y = 20, width = 120, height = 40 },
                fallback = "00:06:00",
                min = "00:00:05",
                max = "01:00:00"
              }
            }
          }
        }
      }
    };

    var createResponse = await client.PostAsJsonAsync("/api/sequences", createPayload).ConfigureAwait(false);
    createResponse.StatusCode.Should().Be(HttpStatusCode.Created);
    var created = await createResponse.Content.ReadFromJsonAsync<JsonElement>().ConfigureAwait(false);
    var sequenceId = created.GetProperty("id").GetString();

    var getResponse = await client.GetAsync(new Uri($"/api/sequences/{sequenceId}", UriKind.Relative)).ConfigureAwait(false);
    var fetched = await getResponse.Content.ReadFromJsonAsync<JsonElement>().ConfigureAwait(false);
    var payload = fetched.GetProperty("steps")[0].GetProperty("primitiveAction").GetProperty("payload");
    payload.GetProperty("option").GetString().Should().Be("Timer");
    var ocr = payload.GetProperty("ocrOffset");
    ocr.GetProperty("region").GetProperty("width").GetInt32().Should().Be(120);
    ocr.GetProperty("fallback").GetString().Should().Be("00:06:00");
  }

  [Fact]
  public async Task OcrOffsetOnNonTimerOptionIsRejected() {
    using var app = CreateFactory();
    var client = AuthedClient(app);

    var createPayload = new {
      name = "self-reschedule-ocr-invalid",
      version = 1,
      steps = new object[] {
        new {
          stepId = "reschedule",
          stepType = "Action",
          primitiveAction = new {
            type = "reschedule-self",
            schemaVersion = "1",
            payload = new {
              option = "OncePerRun",
              ocrOffset = new {
                region = new { x = 10, y = 20, width = 120, height = 40 },
                fallback = "00:06:00"
              }
            }
          }
        }
      }
    };

    var response = await client.PostAsJsonAsync("/api/sequences", createPayload).ConfigureAwait(false);
    response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
  }

  [Fact]
  public async Task OcrOffsetMissingFallbackIsRejected() {
    using var app = CreateFactory();
    var client = AuthedClient(app);

    var createPayload = new {
      name = "self-reschedule-ocr-nofallback",
      version = 1,
      steps = new object[] {
        new {
          stepId = "reschedule",
          stepType = "Action",
          primitiveAction = new {
            type = "reschedule-self",
            schemaVersion = "1",
            payload = new {
              option = "Timer",
              ocrOffset = new { region = new { x = 10, y = 20, width = 120, height = 40 } }
            }
          }
        }
      }
    };

    var response = await client.PostAsJsonAsync("/api/sequences", createPayload).ConfigureAwait(false);
    response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
  }

  // ── T034 (US2): validation rejects malformed option/timer combinations ────

  [Theory]
  [InlineData("Timer", null, null)]                 // Timer with neither field
  [InlineData("Timer", "14:30:00", "00:10:00")]     // Timer with both fields
  [InlineData("OncePerRun", null, "00:10:00")]      // timer field on a non-Timer option
  [InlineData("Timer", null, "-00:10:00")]          // negative offset
  [InlineData("Bogus", null, null)]                 // unknown option
  public async Task MalformedRescheduleSelfPayloadsAreRejected(string option, string? timeOfDay, string? relativeOffset) {
    using var app = CreateFactory();
    var client = AuthedClient(app);

    object payload = (timeOfDay, relativeOffset) switch {
      (not null, not null) => new { option, timerTimeOfDay = timeOfDay, timerRelativeOffset = relativeOffset },
      (not null, null) => new { option, timerTimeOfDay = timeOfDay },
      (null, not null) => new { option, timerRelativeOffset = relativeOffset },
      _ => new { option }
    };

    var createPayload = new {
      name = "self-reschedule-invalid",
      version = 1,
      steps = new object[] {
        new { stepId = "reschedule", stepType = "Action",
          primitiveAction = new { type = "reschedule-self", schemaVersion = "1", payload } }
      }
    };

    var response = await client.PostAsJsonAsync("/api/sequences", createPayload).ConfigureAwait(false);
    response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
  }
}
