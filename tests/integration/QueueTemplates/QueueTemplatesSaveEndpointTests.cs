using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace GameBot.IntegrationTests.QueueTemplates;

[Collection("ConfigIsolation")]
public sealed class QueueTemplatesSaveEndpointTests {
  public QueueTemplatesSaveEndpointTests() {
    Environment.SetEnvironmentVariable("GAMEBOT_USE_ADB", "false");
    Environment.SetEnvironmentVariable("GAMEBOT_AUTH_TOKEN", "test-token");
    TestEnvironment.PrepareCleanDataDir();
  }

  private static HttpClient NewClient(WebApplicationFactory<Program> app) {
    var client = app.CreateClient();
    client.DefaultRequestHeaders.Add("Authorization", "Bearer test-token");
    return client;
  }

  private static Task<HttpResponseMessage> SaveAsync(HttpClient client, string name, string[] sequenceIds, bool overwrite) {
    var entries = Array.ConvertAll(sequenceIds, id => new { sequenceId = id });
    return client.PostAsJsonAsync(new Uri("/api/queue-templates", UriKind.Relative), new { name, entries, overwrite });
  }

  private static async Task<JsonElement> BodyAsync(HttpResponseMessage resp) =>
    JsonDocument.Parse(await resp.Content.ReadAsStringAsync().ConfigureAwait(true)).RootElement.Clone();

  [Fact]
  public async Task NewNameReturns201WithEntriesInOrder() {
    using var app = new WebApplicationFactory<Program>();
    var client = NewClient(app);
    var ids = new[] { "seq-a", "seq-b", "seq-a" };

    var resp = await SaveAsync(client, "Daily Farm", ids, false).ConfigureAwait(true);
    resp.StatusCode.Should().Be(HttpStatusCode.Created);
    var body = await BodyAsync(resp).ConfigureAwait(true);
    body.GetProperty("name").GetString().Should().Be("Daily Farm");
    var entries = body.GetProperty("entries");
    entries.GetArrayLength().Should().Be(3);
    entries[0].GetProperty("sequenceId").GetString().Should().Be("seq-a");
    entries[2].GetProperty("sequenceId").GetString().Should().Be("seq-a");
  }

  [Fact]
  public async Task DuplicateNameWithoutOverwriteReturns409CaseInsensitive() {
    using var app = new WebApplicationFactory<Program>();
    var client = NewClient(app);
    var first = new[] { "seq-a" };
    var second = new[] { "seq-b" };
    await SaveAsync(client, "Daily Farm", first, false).ConfigureAwait(true);

    var resp = await SaveAsync(client, "daily farm", second, false).ConfigureAwait(true);
    resp.StatusCode.Should().Be(HttpStatusCode.Conflict);
    var body = await BodyAsync(resp).ConfigureAwait(true);
    body.GetProperty("error").GetProperty("code").GetString().Should().Be("template_exists");
  }

  [Fact]
  public async Task OverwriteReplacesEntriesAndReturns200() {
    using var app = new WebApplicationFactory<Program>();
    var client = NewClient(app);
    var first = new[] { "seq-a" };
    var replacement = new[] { "seq-x", "seq-y" };
    await SaveAsync(client, "Daily Farm", first, false).ConfigureAwait(true);

    var resp = await SaveAsync(client, "Daily Farm", replacement, true).ConfigureAwait(true);
    resp.StatusCode.Should().Be(HttpStatusCode.OK);
    var body = await BodyAsync(resp).ConfigureAwait(true);
    var entries = body.GetProperty("entries");
    entries.GetArrayLength().Should().Be(2);
    entries[0].GetProperty("sequenceId").GetString().Should().Be("seq-x");
  }

  [Theory]
  [InlineData("")]
  [InlineData("   ")]
  [InlineData("bad/name")]
  [InlineData("name*with*stars")]
  public async Task InvalidNameReturns400(string name) {
    using var app = new WebApplicationFactory<Program>();
    var client = NewClient(app);
    var ids = new[] { "seq-a" };

    var resp = await SaveAsync(client, name, ids, false).ConfigureAwait(true);
    resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
  }

  [Fact]
  public async Task NameOver100CharsReturns400() {
    using var app = new WebApplicationFactory<Program>();
    var client = NewClient(app);

    var resp = await SaveAsync(client, new string('a', 101), Array.Empty<string>(), false).ConfigureAwait(true);
    resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
  }

  [Fact]
  public async Task EmptySequenceIdsIsAllowed() {
    using var app = new WebApplicationFactory<Program>();
    var client = NewClient(app);

    var resp = await SaveAsync(client, "Empty", Array.Empty<string>(), false).ConfigureAwait(true);
    resp.StatusCode.Should().Be(HttpStatusCode.Created);
    var body = await BodyAsync(resp).ConfigureAwait(true);
    body.GetProperty("entries").GetArrayLength().Should().Be(0);
  }
}
