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
public sealed class QueueTemplatesDeleteEndpointTests {
  public QueueTemplatesDeleteEndpointTests() {
    Environment.SetEnvironmentVariable("GAMEBOT_USE_ADB", "false");
    Environment.SetEnvironmentVariable("GAMEBOT_AUTH_TOKEN", "test-token");
    TestEnvironment.PrepareCleanDataDir();
  }

  private static HttpClient NewClient(WebApplicationFactory<Program> app) {
    var client = app.CreateClient();
    client.DefaultRequestHeaders.Add("Authorization", "Bearer test-token");
    return client;
  }

  [Fact]
  public async Task DeleteRemovesTemplateAndReturns204() {
    using var app = new WebApplicationFactory<Program>();
    var client = NewClient(app);
    var ids = new[] { "seq-a" };
    var createResp = await client.PostAsJsonAsync(new Uri("/api/queue-templates", UriKind.Relative),
      new { name = "Daily Farm", sequenceIds = ids, overwrite = false }).ConfigureAwait(true);
    var id = JsonDocument.Parse(await createResp.Content.ReadAsStringAsync().ConfigureAwait(true)).RootElement.GetProperty("id").GetString();

    var resp = await client.DeleteAsync(new Uri($"/api/queue-templates/{id}", UriKind.Relative)).ConfigureAwait(true);
    resp.StatusCode.Should().Be(HttpStatusCode.NoContent);

    var getResp = await client.GetAsync(new Uri($"/api/queue-templates/{id}", UriKind.Relative)).ConfigureAwait(true);
    getResp.StatusCode.Should().Be(HttpStatusCode.NotFound);
  }

  [Fact]
  public async Task DeleteUnknownIdReturns404() {
    using var app = new WebApplicationFactory<Program>();
    var client = NewClient(app);

    var resp = await client.DeleteAsync(new Uri("/api/queue-templates/nope", UriKind.Relative)).ConfigureAwait(true);
    resp.StatusCode.Should().Be(HttpStatusCode.NotFound);
  }
}
