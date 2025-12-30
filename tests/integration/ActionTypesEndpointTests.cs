using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Linq;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace GameBot.IntegrationTests;

public sealed class ActionTypesEndpointTests {
  public ActionTypesEndpointTests() {
    Environment.SetEnvironmentVariable("GAMEBOT_USE_ADB", "false");
    Environment.SetEnvironmentVariable("GAMEBOT_AUTH_TOKEN", "test-token");
    TestEnvironment.PrepareCleanDataDir();
  }

  [Fact]
  public async Task CatalogIncludesConnectToGameDefinition() {
    using var app = new WebApplicationFactory<Program>();
    var client = app.CreateClient();
    client.DefaultRequestHeaders.Add("Authorization", "Bearer test-token");

    var resp = await client.GetAsync(new Uri("/api/action-types", UriKind.Relative)).ConfigureAwait(true);

    resp.StatusCode.Should().Be(HttpStatusCode.OK);
    var payload = await resp.Content.ReadFromJsonAsync<JsonElement>().ConfigureAwait(true);

    payload.TryGetProperty("items", out var items).Should().BeTrue();
    items.ValueKind.Should().Be(JsonValueKind.Array);

    var connect = items.EnumerateArray()
      .FirstOrDefault(i => string.Equals(i.GetProperty("key").GetString(), "connect-to-game", StringComparison.OrdinalIgnoreCase));

    connect.ValueKind.Should().Be(JsonValueKind.Object);
    var displayName = connect.GetProperty("displayName").GetString();
    displayName.Should().NotBeNull();
    displayName!.Contains("connect", StringComparison.OrdinalIgnoreCase).Should().BeTrue();

    var attrDefs = connect.GetProperty("attributeDefinitions");
    attrDefs.ValueKind.Should().Be(JsonValueKind.Array);
    var adbSerial = attrDefs.EnumerateArray()
      .FirstOrDefault(a => string.Equals(a.GetProperty("key").GetString(), "adbSerial", StringComparison.OrdinalIgnoreCase));

    adbSerial.ValueKind.Should().Be(JsonValueKind.Object);
    adbSerial.GetProperty("required").GetBoolean().Should().BeTrue();
    adbSerial.GetProperty("dataType").GetString().Should().Be("string");
  }
}