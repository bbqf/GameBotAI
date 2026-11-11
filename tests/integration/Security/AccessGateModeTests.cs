using GameBot.Service;
using System.Net;
using System.Text.Json;
using FluentAssertions;
using GameBot.Service.Security;
using Xunit;
using static GameBot.IntegrationTests.Security.AuthTestFlows;

namespace GameBot.IntegrationTests.Security;

public sealed class AccessGateOffModeTests {
  [Fact]
  public async Task InternetGetsA2AndARecordWithTheInstruction() {
    using var host = new AuthTestHost(enabled: false, token: null);
    using var client = NewClient(host);

    var (status, body) = await GetAsync(client, ProbePath, remote: "8.8.8.8", host: "bot.example.com").ConfigureAwait(true);

    status.Should().Be(HttpStatusCode.Forbidden);
    body.Should().Contain("remote_access_refused");
    RecordLines(host).Should().ContainSingle().Which.Should().Contain("GAMEBOT_AUTH_ENABLED=true");
  }

  [Fact]
  public async Task LocalMachineAndHomeNetworkPassWithNoToken() {
    using var host = new AuthTestHost(enabled: false, token: null);
    using var client = NewClient(host);

    (await GetAsync(client, ProbePath).ConfigureAwait(true)).Status.Should().Be(HttpStatusCode.OK);
    (await GetAsync(client, ProbePath, remote: "192.168.1.30", host: "192.168.1.20:8080").ConfigureAwait(true)).Status
      .Should().Be(HttpStatusCode.OK);
  }

  [Fact]
  public async Task TokenRuleIsTheSameAsBeforeWhenATokenIsConfigured() {
    using var host = new AuthTestHost(enabled: false);
    using var client = NewClient(host);

    (await GetAsync(client, ProbePath).ConfigureAwait(true)).Status.Should().Be(HttpStatusCode.Unauthorized);
    (await GetAsync(client, ProbePath, bearer: "wrong").ConfigureAwait(true)).Status.Should().Be(HttpStatusCode.Unauthorized);
    (await GetAsync(client, ProbePath, bearer: AuthTestConstants.Token).ConfigureAwait(true)).Status.Should().Be(HttpStatusCode.OK);
  }

  [Fact]
  public async Task HealthIsOpenFromAllSources() {
    using var host = new AuthTestHost(enabled: false);
    using var client = NewClient(host);

    (await GetAsync(client, "/health", remote: "8.8.8.8", host: "bot.example.com").ConfigureAwait(true)).Status
      .Should().Be(HttpStatusCode.OK);
  }
}

public sealed class AccessGateOnModeTests {
  [Theory]
  [InlineData("/api/version")]
  [InlineData("/api/sessions")]
  [InlineData("/api/config")]
  [InlineData("/swagger/v1/swagger.json")]
  public async Task NoSessionGetsA1(string path) {
    using var host = new AuthTestHost();
    using var client = NewClient(host);

    var (status, body) = await GetAsync(client, path).ConfigureAwait(true);

    status.Should().Be(HttpStatusCode.Unauthorized);
    body.Should().Contain("Sign-in is necessary.");
  }

  [Fact]
  public async Task PostWithNoSessionGetsA1() {
    using var host = new AuthTestHost();
    using var client = NewClient(host);

    using var response = await PostAsync(client, "/api/sessions", new { }).ConfigureAwait(true);

    response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
  }

  [Fact]
  public async Task CorrectTokenPassesOnlyFromTheLocalMachineAndTheHomeNetwork() {
    using var host = new AuthTestHost();
    using var client = NewClient(host);
    var token = AuthTestConstants.Token;

    (await GetAsync(client, ProbePath, bearer: token).ConfigureAwait(true)).Status.Should().Be(HttpStatusCode.OK);
    (await GetAsync(client, ProbePath, remote: "192.168.1.30", host: "192.168.1.20:8080", bearer: token).ConfigureAwait(true)).Status
      .Should().Be(HttpStatusCode.OK);
    (await GetAsync(client, ProbePath, remote: "8.8.8.8", host: "bot.example.com", bearer: token).ConfigureAwait(true)).Status
      .Should().Be(HttpStatusCode.Unauthorized);
  }

  [Fact]
  public async Task CorrectTokenFromLoopbackWithForwardHeaderGetsA1() {
    using var host = new AuthTestHost();
    using var client = NewClient(host);
    using var request = AuthTestHost.Request(HttpMethod.Get, ProbePath, "127.0.0.1", "localhost", AuthTestConstants.Token);
    request.Headers.TryAddWithoutValidation("X-Forwarded-For", "203.0.113.9");

    using var response = await client.SendAsync(request).ConfigureAwait(true);

    response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
  }

  [Fact]
  public async Task FiveWrongTokensBlockOneSourceOnly() {
    using var host = new AuthTestHost();
    using var client = NewClient(host);

    for (var i = 0; i < 5; i++) {
      (await GetAsync(client, ProbePath, remote: "192.168.1.31", host: "192.168.1.20", bearer: "wrong").ConfigureAwait(true)).Status
        .Should().Be(HttpStatusCode.Unauthorized);
    }

    (await GetAsync(client, ProbePath, remote: "192.168.1.31", host: "192.168.1.20", bearer: AuthTestConstants.Token).ConfigureAwait(true)).Status
      .Should().Be(HttpStatusCode.Unauthorized);
    (await GetAsync(client, ProbePath, remote: "192.168.1.32", host: "192.168.1.20", bearer: AuthTestConstants.Token).ConfigureAwait(true)).Status
      .Should().Be(HttpStatusCode.OK);
    RecordLines(host).Should().Contain(l => l.Contains("source_blocked", StringComparison.Ordinal));
  }

  [Fact]
  public async Task RequestsWithNoTokenDoNotCountForTheLimiter() {
    using var host = new AuthTestHost();
    using var client = NewClient(host);

    for (var i = 0; i < 8; i++) {
      await GetAsync(client, ProbePath, remote: "192.168.1.31", host: "192.168.1.20").ConfigureAwait(true);
    }

    (await GetAsync(client, ProbePath, remote: "192.168.1.31", host: "192.168.1.20", bearer: AuthTestConstants.Token).ConfigureAwait(true)).Status
      .Should().Be(HttpStatusCode.OK);
  }

  [Fact]
  public async Task OptionsHealthAndTheWebUiShellAreOpen() {
    using var host = new AuthTestHost();
    using var client = NewClient(host);

    using var preflight = AuthTestHost.Request(HttpMethod.Options, ProbePath);
    using var preflightResponse = await client.SendAsync(preflight).ConfigureAwait(true);
    preflightResponse.StatusCode.Should().NotBe(HttpStatusCode.Unauthorized);
    (await GetAsync(client, "/health").ConfigureAwait(true)).Status.Should().Be(HttpStatusCode.OK);
    (await GetAsync(client, "/").ConfigureAwait(true)).Status.Should().Be(HttpStatusCode.OK);
    (await GetAsync(client, "/register").ConfigureAwait(true)).Status.Should().NotBe(HttpStatusCode.Unauthorized);
  }

  [Fact]
  public async Task PublicOriginThatIsNotValidClosesSignInAndKeepsTokenAccess() {
    using var host = new AuthTestHost(publicOrigin: "http://bot.example.com");
    using var client = NewClient(host);

    using var options = await PostAsync(client, ApiRoutes.AuthSignInOptions, null).ConfigureAwait(true);
    options.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    (await GetAsync(client, ProbePath, bearer: AuthTestConstants.Token).ConfigureAwait(true)).Status.Should().Be(HttpStatusCode.OK);
  }

  [Fact]
  public async Task StatusReportsTheSourceAndHidesTheStateOfPasskeys() {
    using var host = new AuthTestHost();
    using var client = NewClient(host);

    var (status, body) = await GetAsync(client, ApiRoutes.AuthStatus).ConfigureAwait(true);

    status.Should().Be(HttpStatusCode.OK);
    using var doc = JsonDocument.Parse(body);
    var root = doc.RootElement;
    root.GetProperty("signInEnabled").GetBoolean().Should().BeTrue();
    root.GetProperty("signedIn").GetBoolean().Should().BeFalse();
    root.GetProperty("source").GetString().Should().Be("localMachine");
    root.GetProperty("tokenAccessHere").GetBoolean().Should().BeTrue();
    root.GetProperty("publicOrigin").GetString().Should().Be(AuthTestConstants.PublicOrigin);
    root.EnumerateObject().Select(p => p.Name).Should().BeEquivalentTo(
      "signInEnabled", "signedIn", "passkeysSupportedHere", "source", "publicOrigin", "tokenAccessHere");
  }
}
