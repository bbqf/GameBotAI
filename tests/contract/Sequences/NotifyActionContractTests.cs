using System;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace GameBot.ContractTests.Sequences;

/// <summary>
/// Feature 087 (issue #181), User Story 3: the <c>notify</c> action round-trips through the sequence
/// authoring API, and a malformed one is rejected cleanly.
/// <para>
/// The load-bearing test here is <see cref="MalformedNotifyActionIsRejectedWith400Not500"/>. A new
/// action type must be registered in <b>both</b> <c>SequenceStepValidationService</c> and
/// <c>FileSequenceRepository.ValidateActionPayloads</c>; missing either produces a 500 at save time
/// instead of an actionable rejection, which is a mistake this repository has made before.
/// </para>
/// </summary>
public sealed class NotifyActionContractTests {
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

  private static object NotifyStep(object payload) => new {
    stepId = "alert",
    label = "Raise an alert",
    stepType = "Action",
    primitiveAction = new { type = "notify", schemaVersion = "1", payload }
  };

  [Fact]
  public async Task NotifyActionRoundTripsThroughAuthoring() {
    using var app = CreateFactory();
    var client = AuthedClient(app);

    var payload = new {
      name = "notify-contract-" + Guid.NewGuid().ToString("N"),
      version = 1,
      steps = new object[] {
        NotifyStep(new { message = "Unrecognised screen; BACK did not dismiss it." })
      }
    };

    var create = await client.PostAsJsonAsync("/api/sequences", payload).ConfigureAwait(false);
    create.StatusCode.Should().Be(HttpStatusCode.Created);
    var id = (await create.Content.ReadFromJsonAsync<JsonElement>().ConfigureAwait(false))
      .GetProperty("id").GetString();

    var get = await client.GetAsync(new Uri($"/api/sequences/{id}", UriKind.Relative)).ConfigureAwait(false);
    var fetched = await get.Content.ReadFromJsonAsync<JsonElement>().ConfigureAwait(false);
    var step = fetched.GetProperty("steps")[0];
    step.GetProperty("primitiveAction").GetProperty("type").GetString().Should().Be("notify");
    step.GetProperty("primitiveAction").GetProperty("payload").GetProperty("message").GetString()
      .Should().Be("Unrecognised screen; BACK did not dismiss it.");
  }

  [Fact]
  public async Task NotifyActionWithPerStepUrlRoundTrips() {
    using var app = CreateFactory();
    var client = AuthedClient(app);

    var payload = new {
      name = "notify-url-" + Guid.NewGuid().ToString("N"),
      version = 1,
      steps = new object[] {
        NotifyStep(new { message = "escalating", url = "https://alerts.example/hook" })
      }
    };

    var create = await client.PostAsJsonAsync("/api/sequences", payload).ConfigureAwait(false);
    create.StatusCode.Should().Be(HttpStatusCode.Created);
    var id = (await create.Content.ReadFromJsonAsync<JsonElement>().ConfigureAwait(false))
      .GetProperty("id").GetString();

    var get = await client.GetAsync(new Uri($"/api/sequences/{id}", UriKind.Relative)).ConfigureAwait(false);
    var fetched = await get.Content.ReadFromJsonAsync<JsonElement>().ConfigureAwait(false);
    fetched.GetProperty("steps")[0].GetProperty("primitiveAction").GetProperty("payload")
      .GetProperty("url").GetString().Should().Be("https://alerts.example/hook");
  }

  /// <summary>
  /// A malformed notify step must be an actionable 400, never a 500. A 500 here means the action
  /// type reached the repository's payload validation without being registered in its allow-list.
  /// </summary>
  [Fact]
  public async Task MalformedNotifyActionIsRejectedWith400Not500() {
    using var app = CreateFactory();
    var client = AuthedClient(app);

    var payload = new {
      name = "notify-bad-" + Guid.NewGuid().ToString("N"),
      version = 1,
      steps = new object[] { NotifyStep(new { message = "" }) }
    };

    var create = await client.PostAsJsonAsync("/api/sequences", payload).ConfigureAwait(false);

    create.StatusCode.Should().Be(HttpStatusCode.BadRequest,
      "a malformed notify step is a save-time rejection, not a server fault");
    (await create.Content.ReadAsStringAsync().ConfigureAwait(false))
      .Should().Contain("message");
  }

  [Fact]
  public async Task NotifyActionWithMalformedUrlIsRejected() {
    using var app = CreateFactory();
    var client = AuthedClient(app);

    var payload = new {
      name = "notify-badurl-" + Guid.NewGuid().ToString("N"),
      version = 1,
      steps = new object[] { NotifyStep(new { message = "escalating", url = "localhost:9099" }) }
    };

    var create = await client.PostAsJsonAsync("/api/sequences", payload).ConfigureAwait(false);

    create.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    (await create.Content.ReadAsStringAsync().ConfigureAwait(false))
      .Should().Contain("absolute http or https");
  }
}
