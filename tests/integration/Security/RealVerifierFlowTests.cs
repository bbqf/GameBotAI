using System.Net;
using System.Text.Json;
using FluentAssertions;
using GameBot.Service;
using GameBot.Service.Security;
using Microsoft.AspNetCore.WebUtilities;
using Xunit;
using static GameBot.IntegrationTests.Security.AuthTestFlows;

namespace GameBot.IntegrationTests.Security;

/// <summary>Registration and sign-in with the real Fido2 verifier and a software authenticator (feature 187, research R15).</summary>
public sealed class RealVerifierFlowTests {
  private const string Origin = AuthTestConstants.PublicOrigin;
  private const string Proxy = "192.168.1.5";

  private static async Task<(string CeremonyId, JsonElement PublicKey)> OptionsAsync(
    HttpClient client, string path, object? body, Action<HttpRequestMessage>? configure, string? remote) {
    using var response = await PostAsync(client, path, body, remote, configure: configure).ConfigureAwait(false);
    response.EnsureSuccessStatusCode();
    var json = await ReadJsonAsync(response).ConfigureAwait(false);
    return (json.GetProperty("ceremonyId").GetString()!, json.GetProperty("publicKey"));
  }

  private static async Task<byte[]> RegisterAsync(
    AuthTestHost host, HttpClient client, SoftwareAuthenticator authenticator, Action<HttpRequestMessage>? configure = null, string? remote = null) {
    var code = new RegistrationCodeStore(host.AuthDir, TimeProvider.System).NewCode();
    var (ceremony, publicKey) = await OptionsAsync(client, ApiRoutes.AuthRegisterOptions, new { code }, configure, remote).ConfigureAwait(false);
    var handle = WebEncoders.Base64UrlDecode(publicKey.GetProperty("user").GetProperty("id").GetString()!);
    using var complete = await PostAsync(client, ApiRoutes.AuthRegisterComplete, new {
      ceremonyId = ceremony,
      name = "Software key",
      credential = authenticator.CreateRegistration(publicKey, Origin)
    }, remote, configure: configure).ConfigureAwait(false);
    complete.StatusCode.Should().Be(HttpStatusCode.NoContent);
    return handle;
  }

  private static async Task<HttpResponseMessage> SignInAsync(
    HttpClient client, SoftwareAuthenticator authenticator, byte[] handle, Action<HttpRequestMessage>? configure = null,
    string? remote = null, string origin = Origin, bool useWrongChallenge = false, bool breakSignature = false) {
    var (ceremony, publicKey) = await OptionsAsync(client, ApiRoutes.AuthSignInOptions, null, configure, remote).ConfigureAwait(false);
    var other = useWrongChallenge ? WebEncoders.Base64UrlEncode(new byte[32]) : null;
    return await PostAsync(client, ApiRoutes.AuthSignInComplete, new {
      ceremonyId = ceremony,
      credential = authenticator.CreateAssertion(publicKey, origin, handle, other, breakSignature)
    }, remote, configure: configure).ConfigureAwait(false);
  }

  [Fact]
  public async Task RegistrationAndSignInWorkWithTheRealVerifier() {
    using var host = new AuthTestHost();
    using var client = NewClient(host);
    using var authenticator = new SoftwareAuthenticator();

    var handle = await RegisterAsync(host, client, authenticator).ConfigureAwait(true);
    using var response = await SignInAsync(client, authenticator, handle).ConfigureAwait(true);

    response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    CookieOf(response).Should().NotBeNull();
  }

  [Fact]
  public async Task SecondSignInWithAHigherCounterWorks() {
    using var host = new AuthTestHost();
    using var client = NewClient(host);
    using var authenticator = new SoftwareAuthenticator();
    var handle = await RegisterAsync(host, client, authenticator).ConfigureAwait(true);

    using var first = await SignInAsync(client, authenticator, handle).ConfigureAwait(true);
    using var second = await SignInAsync(client, authenticator, handle).ConfigureAwait(true);

    first.StatusCode.Should().Be(HttpStatusCode.NoContent);
    second.StatusCode.Should().Be(HttpStatusCode.NoContent);
  }

  [Fact]
  public async Task WrongOriginWrongChallengeAndChangedSignatureGetA1() {
    using var host = new AuthTestHost();
    using var client = NewClient(host);
    using var authenticator = new SoftwareAuthenticator();
    var handle = await RegisterAsync(host, client, authenticator).ConfigureAwait(true);

    using var wrongOrigin = await SignInAsync(client, authenticator, handle, origin: "https://evil.example.org", remote: "203.0.113.1").ConfigureAwait(true);
    using var wrongChallenge = await SignInAsync(client, authenticator, handle, useWrongChallenge: true, remote: "203.0.113.2").ConfigureAwait(true);
    using var brokenSignature = await SignInAsync(client, authenticator, handle, breakSignature: true, remote: "203.0.113.3").ConfigureAwait(true);

    wrongOrigin.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    wrongChallenge.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    brokenSignature.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
  }

  [Fact]
  public async Task RegistrationWithAWrongOriginGetsA1AndTheCodeStaysActive() {
    using var host = new AuthTestHost();
    using var client = NewClient(host);
    using var authenticator = new SoftwareAuthenticator();
    var code = new RegistrationCodeStore(host.AuthDir, TimeProvider.System).NewCode();
    var (ceremony, publicKey) = await OptionsAsync(client, ApiRoutes.AuthRegisterOptions, new { code }, null, null).ConfigureAwait(true);

    using var complete = await PostAsync(client, ApiRoutes.AuthRegisterComplete, new {
      ceremonyId = ceremony,
      credential = authenticator.CreateRegistration(publicKey, "https://evil.example.org")
    }).ConfigureAwait(true);

    complete.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    File.Exists(Path.Combine(host.AuthDir, "registration-code.json")).Should().BeTrue();
  }

  [Theory]
  [InlineData("traefik")]
  [InlineData("nginx")]
  public async Task FlowsWorkThroughATrustedProxyWithEachHeaderProfile(string profile) {
    using var host = new AuthTestHost(trustedProxies: Proxy);
    using var client = NewClient(host);
    using var authenticator = new SoftwareAuthenticator();
    Action<HttpRequestMessage> configure = profile == "traefik"
      ? r => AuthTestHost.ApplyTraefikProfile(r)
      : r => AuthTestHost.ApplyNginxProfile(r);

    var handle = await RegisterAsync(host, client, authenticator, configure, Proxy).ConfigureAwait(true);
    using var response = await SignInAsync(client, authenticator, handle, configure, Proxy).ConfigureAwait(true);

    response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    var cookie = CookieOf(response)!;
    using var request = AuthTestHost.Request(HttpMethod.Get, ProbePath, Proxy);
    configure(request);
    request.Headers.TryAddWithoutValidation("Cookie", cookie);
    using var probe = await client.SendAsync(request).ConfigureAwait(true);
    probe.StatusCode.Should().Be(HttpStatusCode.OK);
  }
}
