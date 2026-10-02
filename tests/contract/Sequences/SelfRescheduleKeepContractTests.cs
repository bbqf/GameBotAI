using System;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace GameBot.ContractTests.Sequences;

/// <summary>
/// Feature 125: the <c>keep</c> option of <c>reschedule-self</c> on create, update and PATCH. The service has no import endpoint.
/// A bad value or a non-Timer option returns 400 on each path, never 500 (FR-006, FR-008). A valid
/// value comes back unchanged (FR-007). The contract tests share the bin data dir, so each test
/// deletes the sequence that it creates.
/// </summary>
public sealed class SelfRescheduleKeepContractTests {
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

  private static readonly object ValidPlain = new { option = "Timer", timerRelativeOffset = "00:10:00" };

  public static TheoryData<string, object> InvalidPayloads => new() {
    { "earliest", new { option = "Timer", timerRelativeOffset = "00:10:00", keep = "latest" } },
    { "keep is only valid when option is Timer", new { option = "OncePerRun", keep = "earliest" } },
    { "keep is only valid when option is Timer", new { option = "Cancel", keep = "earliest" } }
  };

  private static async Task<string> CreateValidAsync(System.Net.Http.HttpClient client, string name) {
    var response = await client.PostAsJsonAsync("/api/sequences", Body(name, ValidPlain)).ConfigureAwait(false);
    response.StatusCode.Should().Be(HttpStatusCode.Created);
    var created = await response.Content.ReadFromJsonAsync<JsonElement>().ConfigureAwait(false);
    return created.GetProperty("id").GetString()!;
  }

  private static async Task DeleteAsync(System.Net.Http.HttpClient client, string id) {
    (await client.DeleteAsync(new Uri($"/api/sequences/{id}", UriKind.Relative)).ConfigureAwait(false)).Dispose();
  }

  [Theory]
  [MemberData(nameof(InvalidPayloads))]
  public async Task CreateRejectsABadKeepWith400(string mention, object payload) {
    using var app = CreateFactory();
    var client = AuthedClient(app);

    var response = await client.PostAsJsonAsync("/api/sequences", Body($"rs-keep-create-{Guid.NewGuid():n}", payload)).ConfigureAwait(false);

    response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    (await response.Content.ReadAsStringAsync().ConfigureAwait(false)).Should().Contain(mention);
  }

  [Theory]
  [MemberData(nameof(InvalidPayloads))]
  public async Task PutRejectsABadKeepWith400(string mention, object payload) {
    using var app = CreateFactory();
    var client = AuthedClient(app);
    var name = $"rs-keep-put-{Guid.NewGuid():n}";
    var id = await CreateValidAsync(client, name).ConfigureAwait(false);
    try {
      var response = await client.PutAsJsonAsync($"/api/sequences/{id}", Body(name, payload)).ConfigureAwait(false);

      response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
      (await response.Content.ReadAsStringAsync().ConfigureAwait(false)).Should().Contain(mention);
    }
    finally {
      await DeleteAsync(client, id).ConfigureAwait(false);
    }
  }

  [Theory]
  [MemberData(nameof(InvalidPayloads))]
  public async Task PatchRejectsABadKeepWith400(string mention, object payload) {
    using var app = CreateFactory();
    var client = AuthedClient(app);
    var name = $"rs-keep-patch-{Guid.NewGuid():n}";
    var id = await CreateValidAsync(client, name).ConfigureAwait(false);
    try {
      var response = await client.PatchAsJsonAsync($"/api/sequences/{id}", Body(name, payload)).ConfigureAwait(false);

      response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
      (await response.Content.ReadAsStringAsync().ConfigureAwait(false)).Should().Contain(mention);
    }
    finally {
      await DeleteAsync(client, id).ConfigureAwait(false);
    }
  }

  [Theory]
  [InlineData("earliest")]
  [InlineData("Earliest")]
  public async Task CreateAcceptsAValidKeepAndReturnsItUnchanged(string keep) {
    using var app = CreateFactory();
    var client = AuthedClient(app);
    var name = $"rs-keep-ok-{Guid.NewGuid():n}";
    var payload = new { option = "Timer", timerRelativeOffset = "00:10:00", keep };

    var response = await client.PostAsJsonAsync("/api/sequences", Body(name, payload)).ConfigureAwait(false);

    response.StatusCode.Should().Be(HttpStatusCode.Created);
    var created = await response.Content.ReadFromJsonAsync<JsonElement>().ConfigureAwait(false);
    var id = created.GetProperty("id").GetString()!;
    try {
      var get = await client.GetAsync(new Uri($"/api/sequences/{id}", UriKind.Relative)).ConfigureAwait(false);
      get.StatusCode.Should().Be(HttpStatusCode.OK);
      var text = await get.Content.ReadAsStringAsync().ConfigureAwait(false);
      text.Should().Contain($"\"keep\":\"{keep}\"");
    }
    finally {
      await DeleteAsync(client, id).ConfigureAwait(false);
    }
  }
}
