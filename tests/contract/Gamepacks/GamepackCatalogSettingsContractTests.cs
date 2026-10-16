using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using Xunit;

#pragma warning disable CA2007, CA1861, CA1707, CA2000, CA1305, CA1307, CA1310, CA1849

namespace GameBot.ContractTests.Gamepacks;

/// <summary>Feature 147 (T022): the settings routes, the token rules, and the API token rule on each route.</summary>
public sealed class GamepackCatalogSettingsContractTests {
  private const string Canary = "CANARY-TOKEN-9f3b1c7a-do-not-leak";

  [Fact]
  public async Task SettingsRoundTripWithTokenSetOnly() {
    using var host = new GamepackContractHost();

    var put = await host.PutCatalogSettingsAsync("https://catalog.example/gp", allowInsecure: false, token: Canary);
    put.StatusCode.Should().Be(HttpStatusCode.OK);
    var putBody = await put.Content.ReadAsStringAsync();
    var get = await host.Client.GetAsync(new Uri("/api/gamepacks/catalog/settings", UriKind.Relative));
    var getBody = await get.Content.ReadAsStringAsync();

    foreach (var body in new[] { putBody, getBody }) {
      body.Should().NotContain(Canary);
      var json = JsonDocument.Parse(body).RootElement;
      json.GetProperty("location").GetString().Should().Be("https://catalog.example/gp");
      json.GetProperty("allowInsecure").GetBoolean().Should().BeFalse();
      json.GetProperty("tokenSet").GetBoolean().Should().BeTrue();
      json.TryGetProperty("token", out _).Should().BeFalse();
    }
  }

  [Fact]
  public async Task TheTokenRulesKeepClearAndSet() {
    using var host = new GamepackContractHost();
    await host.SetCatalogAsync("https://catalog.example/gp", token: "one");

    await host.SetCatalogAsync("https://catalog.example/gp", token: null);
    (await host.GetJsonAsync("/api/gamepacks/catalog/settings")).GetProperty("tokenSet").GetBoolean().Should().BeTrue();

    await host.SetCatalogAsync("https://catalog.example/gp", token: string.Empty);
    (await host.GetJsonAsync("/api/gamepacks/catalog/settings")).GetProperty("tokenSet").GetBoolean().Should().BeFalse();
  }

  [Theory]
  [InlineData("ftp://catalog.example/gp")]
  [InlineData("http://catalog.example/gp")]
  [InlineData("not a location")]
  public async Task ABadLocationGivesInvalidLocation(string location) {
    using var host = new GamepackContractHost();

    var response = await host.PutCatalogSettingsAsync(location);

    response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    (await GamepackContractHost.CodeOfAsync(response)).Should().Be("invalid_location");
  }

  [Fact]
  public async Task HttpIsAllowedForLoopbackAndWithAllowInsecure() {
    using var host = new GamepackContractHost();

    (await host.PutCatalogSettingsAsync("http://localhost:9/gp")).StatusCode.Should().Be(HttpStatusCode.OK);
    (await host.PutCatalogSettingsAsync("http://catalog.example/gp", allowInsecure: true)).StatusCode.Should().Be(HttpStatusCode.OK);
  }

  [Fact]
  public async Task AChangeTakesEffectWithoutARestart() {
    using var host = new GamepackContractHost();
    (await host.CatalogListAsync()).StatusCode.Should().Be(HttpStatusCode.Conflict);

    var folder = Path.Combine(host.Root, "catalog");
    new GameBot.UnitTests.Gamepacks.CatalogFixture().WriteTo(folder);
    await host.SetCatalogAsync(folder);

    (await host.CatalogListAsync()).StatusCode.Should().Be(HttpStatusCode.OK);
  }

  [Fact]
  public async Task NoResponseHoldsTheTokenCanary() {
    using var host = new GamepackContractHost();
    await host.SetCatalogAsync("https://127.0.0.1:1/gp", token: Canary);

    var responses = new[] {
      await host.Client.GetAsync(new Uri("/api/gamepacks/catalog/settings", UriKind.Relative)),
      await host.CatalogListAsync(),
      await host.CatalogInstallAsync("pns-daily", "1.0.0"),
      await host.CatalogInstallAsync(null, null),
      await host.PutCatalogSettingsAsync("ftp://bad", token: Canary)
    };

    foreach (var response in responses) {
      (await response.Content.ReadAsStringAsync()).Should().NotContain(Canary);
    }
  }

  [Theory]
  [InlineData("GET", "/api/gamepacks/catalog/settings")]
  [InlineData("PUT", "/api/gamepacks/catalog/settings")]
  [InlineData("GET", "/api/gamepacks/catalog")]
  [InlineData("POST", "/api/gamepacks/catalog/install")]
  public async Task ACallWithNoTokenIsRefused(string method, string path) {
    using var host = new GamepackContractHost();
    using var anonymous = host.CreateAnonymousClient();
    using var request = new HttpRequestMessage(new HttpMethod(method), new Uri(path, UriKind.Relative));
    if (method is "PUT" or "POST") {
      request.Content = JsonContent.Create(new { location = "https://catalog.example/gp" });
    }

    var response = await anonymous.SendAsync(request);

    response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
  }
}
