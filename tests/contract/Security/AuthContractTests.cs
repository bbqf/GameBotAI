using System.Net;
using System.Text;
using System.Text.Json;
using FluentAssertions;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace GameBot.ContractTests.Security;

/// <summary>Contract tests of the fixed refusals and of the status call (feature 187, contracts/auth-api.md).</summary>
public sealed class AuthContractTests : IDisposable {
  private const string A1 = "{\"error\":{\"code\":\"unauthorized\",\"message\":\"Sign-in is necessary.\",\"hint\":null}}";
  private const string A2 = "{\"error\":{\"code\":\"remote_access_refused\",\"message\":\"The service refuses this request.\",\"hint\":null}}";

  private readonly string _dir = Path.Combine(Path.GetTempPath(), "gamebot-auth-contract-" + Guid.NewGuid().ToString("N"));

  public void Dispose() {
    if (Directory.Exists(_dir)) {
      try {
        Directory.Delete(_dir, recursive: true);
      }
      catch (IOException) {
        // The host can still hold a file. The temporary directory stays.
      }
    }

    GC.SuppressFinalize(this);
  }

  private WebApplicationFactory<Program> Host(bool enabled) {
    var settings = new Dictionary<string, string?> {
      ["Service:Storage:Root"] = _dir,
      ["Service:Auth:Enabled"] = enabled ? "true" : "false",
      ["Service:Auth:PublicOrigin"] = "https://bot.example.com",
      ["Service:Auth:Token"] = "owner-token"
    };
    return new WebApplicationFactory<Program>().WithWebHostBuilder(builder => {
      foreach (var pair in settings) {
        builder.UseSetting(pair.Key, pair.Value);
      }

      builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(settings));
      builder.ConfigureServices(services => services.AddSingleton<IStartupFilter>(new RemoteAddressFilter()));
    });
  }

  private static async Task<(HttpStatusCode Status, byte[] Body)> SendAsync(
    HttpClient client, string path, string? bearer = null, string? cookie = null, string? remote = null) {
    using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(path, UriKind.Relative));
    if (bearer is not null) {
      request.Headers.TryAddWithoutValidation("Authorization", "Bearer " + bearer);
    }

    if (cookie is not null) {
      request.Headers.TryAddWithoutValidation("Cookie", cookie);
    }

    if (remote is not null) {
      request.Headers.TryAddWithoutValidation("X-Test-Remote-Ip", remote);
    }

    using var response = await client.SendAsync(request).ConfigureAwait(false);
    return (response.StatusCode, await response.Content.ReadAsByteArrayAsync().ConfigureAwait(false));
  }

  [Fact]
  public async Task A1BodyIsTheSameBytesForEachCause() {
    using var host = Host(enabled: true);
    using var client = host.CreateClient();

    var none = await SendAsync(client, "/api/version").ConfigureAwait(true);
    var unknownCookie = await SendAsync(client, "/api/version", cookie: "__Host-gamebot-session=bogus").ConfigureAwait(true);
    var wrongToken = await SendAsync(client, "/api/version", bearer: "wrong").ConfigureAwait(true);

    foreach (var result in new[] { none, unknownCookie, wrongToken }) {
      result.Status.Should().Be(HttpStatusCode.Unauthorized);
      Encoding.UTF8.GetString(result.Body).Should().Be(A1);
    }

    none.Body.Should().Equal(unknownCookie.Body).And.Equal(wrongToken.Body);
  }

  [Fact]
  public async Task A1BodyIsTheSameForABlockedSource() {
    using var host = Host(enabled: true);
    using var client = host.CreateClient();
    for (var i = 0; i < 5; i++) {
      await SendAsync(client, "/api/version", bearer: "wrong").ConfigureAwait(true);
    }

    var blocked = await SendAsync(client, "/api/version", bearer: "owner-token").ConfigureAwait(true);

    blocked.Status.Should().Be(HttpStatusCode.Unauthorized);
    Encoding.UTF8.GetString(blocked.Body).Should().Be(A1);
  }

  [Fact]
  public async Task A2BodyMatchesTheContract() {
    using var host = Host(enabled: false);
    using var client = host.CreateClient();

    var refused = await SendAsync(client, "/api/version", remote: "8.8.8.8").ConfigureAwait(true);

    refused.Status.Should().Be(HttpStatusCode.Forbidden);
    Encoding.UTF8.GetString(refused.Body).Should().Be(A2);
  }

  [Fact]
  public async Task StatusHasTheFieldsOfTheContractAndNothingAboutPasskeysOrCodes() {
    using var host = Host(enabled: true);
    using var client = host.CreateClient();

    var (status, body) = await SendAsync(client, "/api/auth/status").ConfigureAwait(true);

    status.Should().Be(HttpStatusCode.OK);
    using var doc = JsonDocument.Parse(body);
    doc.RootElement.EnumerateObject().Select(p => p.Name).Should().BeEquivalentTo(
      "signInEnabled", "signedIn", "passkeysSupportedHere", "source", "publicOrigin", "tokenAccessHere");
    Encoding.UTF8.GetString(body).Should().NotContainEquivalentOf("passkey count").And.NotContainEquivalentOf("code");
  }
}

/// <summary>Sets the remote address of a request from the test header, as the integration tests do.</summary>
internal sealed class RemoteAddressFilter : IStartupFilter {
  public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app => {
    app.Use(async (context, nextMiddleware) => {
      if (context.Request.Headers.TryGetValue("X-Test-Remote-Ip", out var value) && IPAddress.TryParse(value.ToString(), out var address)) {
        context.Connection.RemoteIpAddress = address;
      }

      await nextMiddleware(context).ConfigureAwait(false);
    });
    next(app);
  };
}
