using System;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

// Test-code analyzer relaxation: the request content is disposed with the HTTP request.
#pragma warning disable CA2000

namespace GameBot.IntegrationTests.Sequences;

/// <summary>Feature 170: <c>onTimeLimit</c> on POST, PUT, PATCH, GET, list, and dry run.</summary>
[Collection("ConfigIsolation")]
public sealed class SequenceOnTimeLimitApiIntegrationTests : OnTimeLimitIntegrationTestBase {
  private const string Member = "onTimeLimit";
  private const string ErrorText = "onTimeLimit must be one of: fail, end.";

  private static string Body(string name, string? memberJson, int version = 1, bool dryRun = false) {
    var member = memberJson is null ? string.Empty : $",\"{Member}\":{memberJson}";
    var dry = dryRun ? ",\"dryRun\":true" : string.Empty;
    return "{\"name\":\"" + name + "\",\"version\":" + version + ",\"steps\":[{\"stepId\":\"t1\",\"label\":\"Tap\",\"stepType\":\"Action\","
      + "\"primitiveAction\":{\"type\":\"tap\",\"schemaVersion\":\"v1\",\"payload\":{\"x\":1,\"y\":1}}}]" + member + dry + "}";
  }

  private static StringContent Json(string json) => new(json, Encoding.UTF8, "application/json");

  private static async Task<JsonElement> ReadAsync(HttpResponseMessage response) =>
    JsonDocument.Parse(await response.Content.ReadAsStringAsync().ConfigureAwait(false)).RootElement.Clone();

  private static async Task<string> CreateAsync(HttpClient client, string name, string? memberJson) {
    var response = await client.PostAsync(new Uri("/api/sequences", UriKind.Relative), Json(Body(name, memberJson))).ConfigureAwait(false);
    response.StatusCode.Should().Be(HttpStatusCode.Created);
    var body = await ReadAsync(response).ConfigureAwait(false);
    body.GetProperty(Member).GetString().Should().Be(memberJson is null ? "fail" : memberJson.Trim('"'));
    return body.GetProperty("id").GetString()!;
  }

  private static async Task<JsonElement> GetAsync(HttpClient client, string id) {
    var response = await client.GetAsync(new Uri($"/api/sequences/{id}", UriKind.Relative)).ConfigureAwait(false);
    response.StatusCode.Should().Be(HttpStatusCode.OK);
    return await ReadAsync(response).ConfigureAwait(false);
  }

  [Theory]
  [InlineData("\"end\"", "end")]
  [InlineData("\"fail\"", "fail")]
  [InlineData(null, "fail")]
  public async Task PostStoresTheValueAndAbsentMeansFail(string? memberJson, string expected) {
    using var app = new WebApplicationFactory<Program>();
    var client = CreateClient(app);

    var id = await CreateAsync(client, "OnLimit-Post-" + Guid.NewGuid().ToString("N"), memberJson).ConfigureAwait(false);

    (await GetAsync(client, id).ConfigureAwait(false)).GetProperty(Member).GetString().Should().Be(expected);
  }

  [Fact]
  public async Task PutAndPatchSetTheValueAbsentOnPutResetsAndAbsentOnPatchKeeps() {
    using var app = new WebApplicationFactory<Program>();
    var client = CreateClient(app);
    var name = "OnLimit-Put-" + Guid.NewGuid().ToString("N");
    var id = await CreateAsync(client, name, "\"fail\"").ConfigureAwait(false);

    var put = await client.PutAsync(new Uri($"/api/sequences/{id}", UriKind.Relative), Json(Body(name, "\"end\"", 1))).ConfigureAwait(false);
    put.StatusCode.Should().Be(HttpStatusCode.OK);
    (await ReadAsync(put).ConfigureAwait(false)).GetProperty(Member).GetString().Should().Be("end");

    var patchAbsent = await client.PatchAsync(new Uri($"/api/sequences/{id}", UriKind.Relative), Json("{\"name\":\"" + name + "-b\"}")).ConfigureAwait(false);
    patchAbsent.StatusCode.Should().Be(HttpStatusCode.OK);
    (await GetAsync(client, id).ConfigureAwait(false)).GetProperty(Member).GetString().Should().Be("end");

    var putAbsent = await client.PutAsync(new Uri($"/api/sequences/{id}", UriKind.Relative), Json(Body(name, null, 3))).ConfigureAwait(false);
    putAbsent.StatusCode.Should().Be(HttpStatusCode.OK);
    (await GetAsync(client, id).ConfigureAwait(false)).GetProperty(Member).GetString().Should().Be("fail");

    var patchEnd = await client.PatchAsync(new Uri($"/api/sequences/{id}", UriKind.Relative), Json($"{{\"{Member}\":\"end\"}}")).ConfigureAwait(false);
    patchEnd.StatusCode.Should().Be(HttpStatusCode.OK);
    (await ReadAsync(patchEnd).ConfigureAwait(false)).GetProperty(Member).GetString().Should().Be("end");
  }

  [Theory]
  [InlineData("\"stop\"")]
  [InlineData("\"End\"")]
  [InlineData("null")]
  [InlineData("true")]
  [InlineData("1")]
  public async Task ABadValueGives400OnPostPutPatchAndDryRun(string memberJson) {
    using var app = new WebApplicationFactory<Program>();
    var client = CreateClient(app);
    var name = "OnLimit-Bad-" + Guid.NewGuid().ToString("N");
    var id = await CreateAsync(client, name, "\"end\"").ConfigureAwait(false);

    var post = await client.PostAsync(new Uri("/api/sequences", UriKind.Relative), Json(Body(name + "-2", memberJson))).ConfigureAwait(false);
    var dry = await client.PostAsync(new Uri("/api/sequences", UriKind.Relative), Json(Body(name + "-3", memberJson, dryRun: true))).ConfigureAwait(false);
    var put = await client.PutAsync(new Uri($"/api/sequences/{id}", UriKind.Relative), Json(Body(name, memberJson, 1))).ConfigureAwait(false);
    var patch = await client.PatchAsync(new Uri($"/api/sequences/{id}", UriKind.Relative), Json($"{{\"{Member}\":{memberJson}}}")).ConfigureAwait(false);

    foreach (var response in new[] { post, dry, put, patch }) {
      response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
      (await response.Content.ReadAsStringAsync().ConfigureAwait(false)).Should().Contain(ErrorText);
    }

    var fetched = await GetAsync(client, id).ConfigureAwait(false);
    fetched.GetProperty(Member).GetString().Should().Be("end");
    fetched.GetProperty("version").GetInt32().Should().Be(1);
  }

  [Fact]
  public async Task ListAndDryRunShowTheMember() {
    using var app = new WebApplicationFactory<Program>();
    var client = CreateClient(app);
    var id = await CreateAsync(client, "OnLimit-List-" + Guid.NewGuid().ToString("N"), "\"end\"").ConfigureAwait(false);

    var list = await ReadAsync(await client.GetAsync(new Uri("/api/sequences", UriKind.Relative)).ConfigureAwait(false)).ConfigureAwait(false);
    var found = false;
    foreach (var item in list.EnumerateArray()) {
      if (item.GetProperty("id").GetString() == id) {
        item.GetProperty(Member).GetString().Should().Be("end");
        found = true;
      }
    }
    found.Should().BeTrue();

    var dry = await client.PostAsync(new Uri("/api/sequences", UriKind.Relative), Json(Body("OnLimit-Dry-" + Guid.NewGuid().ToString("N"), "\"end\"", dryRun: true))).ConfigureAwait(false);
    dry.StatusCode.Should().Be(HttpStatusCode.OK);
  }
}
