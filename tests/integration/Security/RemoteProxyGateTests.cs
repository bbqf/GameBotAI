using GameBot.Service;
using System.Net;
using System.Text.Json;
using FluentAssertions;
using Xunit;
using static GameBot.IntegrationTests.Security.AuthTestFlows;

namespace GameBot.IntegrationTests.Security;

public sealed class RemoteProxyGateTests {
  private const string Proxy = "192.168.1.5";
  private const string Direct = "192.168.1.30";

  private static void Profile(HttpRequestMessage request, string name) {
    if (name == "traefik") {
      AuthTestHost.ApplyTraefikProfile(request);
    }
    else {
      AuthTestHost.ApplyNginxProfile(request);
    }
  }

  private static async Task<(HttpStatusCode Status, string Body)> ViaProxyAsync(
    HttpClient client, string profile, string path, string? bearer = null) {
    using var request = AuthTestHost.Request(HttpMethod.Get, path, Proxy, bearer: bearer);
    Profile(request, profile);
    using var response = await client.SendAsync(request).ConfigureAwait(false);
    return (response.StatusCode, await response.Content.ReadAsStringAsync().ConfigureAwait(false));
  }

  [Theory]
  [InlineData("traefik")]
  [InlineData("nginx")]
  public async Task SignInOffGivesA2ForEachProxyProfile(string profile) {
    using var host = new AuthTestHost(enabled: false, trustedProxies: Proxy);
    using var client = NewClient(host);

    (await ViaProxyAsync(client, profile, ProbePath).ConfigureAwait(true)).Status.Should().Be(HttpStatusCode.Forbidden);
    (await ViaProxyAsync(client, profile, "/health").ConfigureAwait(true)).Status.Should().Be(HttpStatusCode.OK);
  }

  [Theory]
  [InlineData("traefik")]
  [InlineData("nginx")]
  public async Task SignInOnGivesA1AndTokenDoesNotHelp(string profile) {
    using var host = new AuthTestHost(trustedProxies: Proxy);
    using var client = NewClient(host);

    (await ViaProxyAsync(client, profile, ProbePath).ConfigureAwait(true)).Status.Should().Be(HttpStatusCode.Unauthorized);
    (await ViaProxyAsync(client, profile, ProbePath, AuthTestConstants.Token).ConfigureAwait(true)).Status
      .Should().Be(HttpStatusCode.Unauthorized);
    (await ViaProxyAsync(client, profile, "/health").ConfigureAwait(true)).Status.Should().Be(HttpStatusCode.OK);
  }

  [Theory]
  [InlineData("traefik")]
  [InlineData("nginx")]
  public async Task StatusThroughTheProxyIsAnInternetRequestWithPasskeySupport(string profile) {
    using var host = new AuthTestHost(trustedProxies: Proxy);
    using var client = NewClient(host);

    var (status, body) = await ViaProxyAsync(client, profile, StatusPath).ConfigureAwait(true);

    status.Should().Be(HttpStatusCode.OK);
    using var doc = JsonDocument.Parse(body);
    doc.RootElement.GetProperty("source").GetString().Should().Be("internet");
    doc.RootElement.GetProperty("passkeysSupportedHere").GetBoolean().Should().BeTrue();
    doc.RootElement.GetProperty("publicOrigin").ValueKind.Should().Be(JsonValueKind.Null);
    doc.RootElement.GetProperty("tokenAccessHere").GetBoolean().Should().BeFalse();
  }

  private const string StatusPath = "/api/auth/status";

  [Fact]
  public async Task DirectHomeNetworkRequestNeedsTheTokenAndGetsTheAddressInStatus() {
    using var host = new AuthTestHost(trustedProxies: Proxy);
    using var client = NewClient(host);

    (await GetAsync(client, ProbePath, Direct, "192.168.1.20:8080").ConfigureAwait(true)).Status.Should().Be(HttpStatusCode.Unauthorized);
    (await GetAsync(client, ProbePath, Direct, "192.168.1.20:8080", AuthTestConstants.Token).ConfigureAwait(true)).Status
      .Should().Be(HttpStatusCode.OK);

    var (_, body) = await GetAsync(client, StatusPath, Direct, "192.168.1.20:8080").ConfigureAwait(true);
    using var doc = JsonDocument.Parse(body);
    doc.RootElement.GetProperty("source").GetString().Should().Be("homeNetwork");
    doc.RootElement.GetProperty("passkeysSupportedHere").GetBoolean().Should().BeFalse();
    doc.RootElement.GetProperty("publicOrigin").GetString().Should().Be(AuthTestConstants.PublicOrigin);
    doc.RootElement.GetProperty("tokenAccessHere").GetBoolean().Should().BeTrue();
  }

  [Fact]
  public async Task DirectStatusHasNoTokenPromptWhenNoTokenIsConfigured() {
    using var host = new AuthTestHost(trustedProxies: Proxy, token: null);
    using var client = NewClient(host);

    var (_, body) = await GetAsync(client, StatusPath, Direct, "192.168.1.20:8080").ConfigureAwait(true);

    using var doc = JsonDocument.Parse(body);
    doc.RootElement.GetProperty("tokenAccessHere").GetBoolean().Should().BeFalse();
  }

  [Fact]
  public async Task LoopbackGatewayIsAnInternetRequestWhenLoopbackIsTrusted() {
    using var off = new AuthTestHost(enabled: false, trustedProxies: "127.0.0.1,::1", token: null);
    using var offClient = NewClient(off);
    (await GetAsync(offClient, ProbePath, "127.0.0.1", "localhost:8080").ConfigureAwait(true)).Status
      .Should().Be(HttpStatusCode.Forbidden);

    using var on = new AuthTestHost(trustedProxies: "127.0.0.1,::1");
    using var onClient = NewClient(on);
    (await GetAsync(onClient, ProbePath, "127.0.0.1", "localhost:8080", AuthTestConstants.Token).ConfigureAwait(true)).Status
      .Should().Be(HttpStatusCode.Unauthorized);
  }
}
