using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

// Test-code analyzer relaxation: the request content is disposed with the HTTP request.
#pragma warning disable CA2000

namespace GameBot.ContractTests.Sequences;

/// <summary>
/// Feature 122: <c>excludeFromSuccessNotifications</c> on POST, PUT, PATCH, GET and list.
/// </summary>
public sealed class ExcludeFromSuccessNotificationsContractTests {
  private const string Member = "excludeFromSuccessNotifications";

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

  private static string Body(string name, string? memberJson, int version = 1) {
    var member = memberJson is null ? string.Empty : $",\"{Member}\":{memberJson}";
    return "{\"name\":\"" + name + "\",\"version\":" + version + ",\"steps\":[{\"stepId\":\"t1\",\"label\":\"Tap\",\"stepType\":\"Action\","
      + "\"primitiveAction\":{\"type\":\"tap\",\"schemaVersion\":\"v1\",\"payload\":{\"x\":1,\"y\":1}}}]" + member + "}";
  }

  private static StringContent Json(string json) => new(json, Encoding.UTF8, "application/json");

  private static async Task<JsonElement> ReadAsync(HttpResponseMessage response) =>
    await response.Content.ReadFromJsonAsync<JsonElement>().ConfigureAwait(false);

  private static async Task<string> CreateAsync(HttpClient client, string name, string? memberJson) {
    var response = await client.PostAsync(new Uri("/api/sequences", UriKind.Relative), Json(Body(name, memberJson))).ConfigureAwait(false);
    response.StatusCode.Should().Be(HttpStatusCode.Created);
    return (await ReadAsync(response).ConfigureAwait(false)).GetProperty("id").GetString()!;
  }

  private static async Task<JsonElement> GetAsync(HttpClient client, string id) {
    var response = await client.GetAsync(new Uri($"/api/sequences/{id}", UriKind.Relative)).ConfigureAwait(false);
    response.StatusCode.Should().Be(HttpStatusCode.OK);
    return await ReadAsync(response).ConfigureAwait(false);
  }

  [Theory]
  [InlineData("true", true)]
  [InlineData("false", false)]
  [InlineData(null, false)]
  public async Task PostStoresTheValueAndAbsentMeansFalse(string? memberJson, bool expected) {
    using var app = CreateFactory();
    var client = AuthedClient(app);

    var id = await CreateAsync(client, "Exclude-Post-" + Guid.NewGuid().ToString("N"), memberJson).ConfigureAwait(false);

    (await GetAsync(client, id).ConfigureAwait(false)).GetProperty(Member).GetBoolean().Should().Be(expected);
  }

  [Fact]
  public async Task PutAndPatchAcceptTrueAndFalseAndAbsentKeepsTheValue() {
    using var app = CreateFactory();
    var client = AuthedClient(app);
    var name = "Exclude-Put-" + Guid.NewGuid().ToString("N");
    var id = await CreateAsync(client, name, "false").ConfigureAwait(false);

    var put = await client.PutAsync(new Uri($"/api/sequences/{id}", UriKind.Relative), Json(Body(name, "true", 1))).ConfigureAwait(false);
    put.StatusCode.Should().Be(HttpStatusCode.OK);
    (await ReadAsync(put).ConfigureAwait(false)).GetProperty(Member).GetBoolean().Should().BeTrue();

    var putAbsent = await client.PutAsync(new Uri($"/api/sequences/{id}", UriKind.Relative), Json(Body(name, null, 2))).ConfigureAwait(false);
    putAbsent.StatusCode.Should().Be(HttpStatusCode.OK);
    (await GetAsync(client, id).ConfigureAwait(false)).GetProperty(Member).GetBoolean().Should().BeTrue();

    var patchAbsent = await client.PatchAsync(new Uri($"/api/sequences/{id}", UriKind.Relative), Json("{\"name\":\"" + name + "-b\"}")).ConfigureAwait(false);
    patchAbsent.StatusCode.Should().Be(HttpStatusCode.OK);
    (await GetAsync(client, id).ConfigureAwait(false)).GetProperty(Member).GetBoolean().Should().BeTrue();

    var patchOff = await client.PatchAsync(new Uri($"/api/sequences/{id}", UriKind.Relative), Json($"{{\"{Member}\":false}}")).ConfigureAwait(false);
    patchOff.StatusCode.Should().Be(HttpStatusCode.OK);
    (await GetAsync(client, id).ConfigureAwait(false)).GetProperty(Member).GetBoolean().Should().BeFalse();

    var patchOn = await client.PatchAsync(new Uri($"/api/sequences/{id}", UriKind.Relative), Json($"{{\"{Member}\":true}}")).ConfigureAwait(false);
    patchOn.StatusCode.Should().Be(HttpStatusCode.OK);
    (await GetAsync(client, id).ConfigureAwait(false)).GetProperty(Member).GetBoolean().Should().BeTrue();
  }

  [Theory]
  [InlineData("null")]
  [InlineData("\"yes\"")]
  [InlineData("1")]
  public async Task ABadValueGives400OnPostPutAndPatch(string memberJson) {
    using var app = CreateFactory();
    var client = AuthedClient(app);
    var name = "Exclude-Bad-" + Guid.NewGuid().ToString("N");
    var id = await CreateAsync(client, name, "true").ConfigureAwait(false);

    var post = await client.PostAsync(new Uri("/api/sequences", UriKind.Relative), Json(Body(name + "-2", memberJson))).ConfigureAwait(false);
    var put = await client.PutAsync(new Uri($"/api/sequences/{id}", UriKind.Relative), Json(Body(name, memberJson, 1))).ConfigureAwait(false);
    var patch = await client.PatchAsync(new Uri($"/api/sequences/{id}", UriKind.Relative), Json($"{{\"{Member}\":{memberJson}}}")).ConfigureAwait(false);

    foreach (var response in new[] { post, put, patch }) {
      response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
      var text = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
      text.Should().Contain("excludeFromSuccessNotifications must be true or false.");
    }

    // The bad requests change nothing.
    var fetched = await GetAsync(client, id).ConfigureAwait(false);
    fetched.GetProperty(Member).GetBoolean().Should().BeTrue();
    fetched.GetProperty("version").GetInt32().Should().Be(1);
  }

  [Fact]
  public async Task ListShowsTheMember() {
    using var app = CreateFactory();
    var client = AuthedClient(app);
    var id = await CreateAsync(client, "Exclude-List-" + Guid.NewGuid().ToString("N"), "true").ConfigureAwait(false);

    var list = await ReadAsync(await client.GetAsync(new Uri("/api/sequences", UriKind.Relative)).ConfigureAwait(false)).ConfigureAwait(false);

    foreach (var item in list.EnumerateArray()) {
      if (item.GetProperty("id").GetString() == id) {
        item.GetProperty(Member).GetBoolean().Should().BeTrue();
        return;
      }
    }

    throw new InvalidOperationException("The sequence is not in the list.");
  }

  [Fact]
  public async Task AChangeOfTheOptionChangesVersionAndUpdatedAt() {
    using var app = CreateFactory();
    var client = AuthedClient(app);
    var id = await CreateAsync(client, "Exclude-Version-" + Guid.NewGuid().ToString("N"), "false").ConfigureAwait(false);
    var before = await GetAsync(client, id).ConfigureAwait(false);

    var patch = await client.PatchAsync(new Uri($"/api/sequences/{id}", UriKind.Relative), Json($"{{\"{Member}\":true}}")).ConfigureAwait(false);
    patch.StatusCode.Should().Be(HttpStatusCode.OK);
    var after = await ReadAsync(patch).ConfigureAwait(false);

    after.GetProperty("version").GetInt32().Should().BeGreaterThan(before.GetProperty("version").GetInt32());
  }

  [Fact]
  public async Task TheCloneRoundTripKeepsTheValue() {
    using var app = CreateFactory();
    var client = AuthedClient(app);
    var id = await CreateAsync(client, "Exclude-Clone-" + Guid.NewGuid().ToString("N"), "true").ConfigureAwait(false);
    var source = await GetAsync(client, id).ConfigureAwait(false);

    var clone = new System.Collections.Generic.Dictionary<string, object?> {
      ["name"] = "Exclude-Clone-Copy-" + Guid.NewGuid().ToString("N"),
      ["version"] = 1,
      ["steps"] = source.GetProperty("steps"),
      [Member] = source.GetProperty(Member).GetBoolean()
    };
    var created = await client.PostAsJsonAsync("/api/sequences", clone).ConfigureAwait(false);
    created.StatusCode.Should().Be(HttpStatusCode.Created);
    var cloneId = (await ReadAsync(created).ConfigureAwait(false)).GetProperty("id").GetString()!;

    (await GetAsync(client, cloneId).ConfigureAwait(false)).GetProperty(Member).GetBoolean().Should().BeTrue();
  }
}
