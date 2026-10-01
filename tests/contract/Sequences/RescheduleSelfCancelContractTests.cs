using System;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace GameBot.ContractTests.Sequences;

/// <summary>
/// Feature 123: the reschedule-self option Cancel on create, update, PATCH and validate. A bad payload
/// returns 400 on each path, never 500 (FR-010, SC-004). The contract tests share the bin data dir,
/// so each test deletes the sequence that it creates.
/// </summary>
public sealed class RescheduleSelfCancelContractTests {
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

  private static object Body(string name, object payload) => new {
    name,
    version = 1,
    steps = new object[] {
      new { stepId = "reschedule", stepType = "Action",
        primitiveAction = new { type = "reschedule-self", schemaVersion = "1", payload } }
    }
  };

  private static readonly object ValidCancel = new { option = "Cancel" };

  public static TheoryData<string, object> InvalidCancelPayloads => new() {
    { "timerTimeOfDay", new { option = "Cancel", timerTimeOfDay = "11:00" } },
    { "timerRelativeOffset", new { option = "Cancel", timerRelativeOffset = "00:10:00" } },
    { "ocrOffset", new {
        option = "Cancel",
        ocrOffset = new { region = new { x = 1, y = 2, width = 3, height = 4 }, fallback = "00:06:00" }
      } }
  };

  private static async Task<string> CreateValidAsync(System.Net.Http.HttpClient client, string name) {
    var response = await client.PostAsJsonAsync("/api/sequences", Body(name, ValidCancel)).ConfigureAwait(false);
    response.StatusCode.Should().Be(HttpStatusCode.Created);
    var created = await response.Content.ReadFromJsonAsync<JsonElement>().ConfigureAwait(false);
    return created.GetProperty("id").GetString()!;
  }

  private static async Task DeleteAsync(System.Net.Http.HttpClient client, string id) {
    (await client.DeleteAsync(new Uri($"/api/sequences/{id}", UriKind.Relative)).ConfigureAwait(false)).Dispose();
  }

  [Theory]
  [MemberData(nameof(InvalidCancelPayloads))]
  public async Task CreateRejectsCancelWithTimerFieldAndNamesTheField(string field, object payload) {
    using var app = CreateFactory();
    var client = AuthedClient(app);

    var response = await client.PostAsJsonAsync("/api/sequences", Body($"rs-cancel-create-{field}", payload)).ConfigureAwait(false);

    response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    (await response.Content.ReadAsStringAsync().ConfigureAwait(false)).Should().Contain(field);
  }

  [Fact]
  public async Task CreateRejectsUnknownOptionAndListsCancel() {
    using var app = CreateFactory();
    var client = AuthedClient(app);

    var response = await client.PostAsJsonAsync("/api/sequences", Body("rs-cancel-bogus", new { option = "Bogus" })).ConfigureAwait(false);

    response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    (await response.Content.ReadAsStringAsync().ConfigureAwait(false)).Should().Contain("Cancel");
  }

  [Theory]
  [MemberData(nameof(InvalidCancelPayloads))]
  public async Task PutRejectsCancelWithTimerField(string field, object payload) {
    using var app = CreateFactory();
    var client = AuthedClient(app);
    var id = await CreateValidAsync(client, $"rs-cancel-put-{field}").ConfigureAwait(false);
    try {
      var response = await client.PutAsJsonAsync($"/api/sequences/{id}", Body($"rs-cancel-put-{field}", payload)).ConfigureAwait(false);

      response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
      (await response.Content.ReadAsStringAsync().ConfigureAwait(false)).Should().Contain(field);
    }
    finally {
      await DeleteAsync(client, id).ConfigureAwait(false);
    }
  }

  [Theory]
  [MemberData(nameof(InvalidCancelPayloads))]
  public async Task PatchRejectsCancelWithTimerField(string field, object payload) {
    using var app = CreateFactory();
    var client = AuthedClient(app);
    var id = await CreateValidAsync(client, $"rs-cancel-patch-{field}").ConfigureAwait(false);
    try {
      var response = await client.PatchAsJsonAsync($"/api/sequences/{id}", Body($"rs-cancel-patch-{field}", payload)).ConfigureAwait(false);

      response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
      (await response.Content.ReadAsStringAsync().ConfigureAwait(false)).Should().Contain(field);
    }
    finally {
      await DeleteAsync(client, id).ConfigureAwait(false);
    }
  }

  [Fact]
  public async Task ValidateAcceptsASavedCancelSequence() {
    using var app = CreateFactory();
    var client = AuthedClient(app);
    var id = await CreateValidAsync(client, "rs-cancel-validate").ConfigureAwait(false);
    try {
      // The validate call takes a flow body and checks the saved steps of the sequence in addition.
      var flow = new {
        name = "rs-cancel-validate",
        version = 1,
        entryStepId = "reschedule",
        steps = new object[] { new { stepId = "reschedule", label = "Reschedule", stepType = "action", payloadRef = "reschedule-self:cancel" } },
        links = Array.Empty<object>()
      };
      var response = await client.PostAsJsonAsync($"/api/sequences/{id}/validate", flow).ConfigureAwait(false);

      response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync().ConfigureAwait(false));
      var body = await response.Content.ReadFromJsonAsync<JsonElement>().ConfigureAwait(false);
      body.GetProperty("valid").GetBoolean().Should().BeTrue();
    }
    finally {
      await DeleteAsync(client, id).ConfigureAwait(false);
    }
  }
}
