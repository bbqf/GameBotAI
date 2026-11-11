using GameBot.Service;
using System.Net;
using System.Text.Json;
using FluentAssertions;
using GameBot.Service.Security;
using Xunit;
using static GameBot.IntegrationTests.Security.AuthTestFlows;

namespace GameBot.IntegrationTests.Security;

public sealed class RegistrationFlowTests {
  [Fact]
  public async Task CorrectCodeRegistersAPasskeyAndDeletesTheCode() {
    using var host = new AuthTestHost(verifier: new FakePasskeyVerifier());
    using var client = NewClient(host);

    var cookie = await RegisterAsync(host, client).ConfigureAwait(true);

    cookie.Should().StartWith(SessionStore.CookieName + "=");
    File.Exists(Path.Combine(host.AuthDir, "registration-code.json")).Should().BeFalse();
    var passkeys = File.ReadAllText(Path.Combine(host.AuthDir, "passkeys.json"));
    passkeys.Should().Contain("My phone");
    RecordLines(host).Should().Contain(l => l.Contains("passkey_registered", StringComparison.Ordinal));
    (await GetAsync(client, ProbePath, "8.8.8.8", "bot.example.com", cookie: cookie).ConfigureAwait(true)).Status
      .Should().Be(HttpStatusCode.OK);
  }

  [Fact]
  public async Task CodeCannotBeUsedTwice() {
    using var host = new AuthTestHost(verifier: new FakePasskeyVerifier());
    using var client = NewClient(host);
    var code = new RegistrationCodeStore(host.AuthDir, TimeProvider.System).NewCode();
    using var first = await PostAsync(client, ApiRoutes.AuthRegisterOptions, new { code }).ConfigureAwait(true);
    var ceremony = (await ReadJsonAsync(first).ConfigureAwait(true)).GetProperty("ceremonyId").GetString();
    using var complete = await PostAsync(client, ApiRoutes.AuthRegisterComplete, new {
      ceremonyId = ceremony,
      credential = new { id = "AQID", rawId = "AQID", valid = true }
    }).ConfigureAwait(true);
    complete.StatusCode.Should().Be(HttpStatusCode.NoContent);

    using var second = await PostAsync(client, ApiRoutes.AuthRegisterOptions, new { code }).ConfigureAwait(true);

    second.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
  }

  [Theory]
  [InlineData("WRONG-CODE")]
  [InlineData("")]
  public async Task WrongOrAbsentCodeGetsA1AndNoPasskey(string code) {
    using var host = new AuthTestHost(verifier: new FakePasskeyVerifier());
    using var client = NewClient(host);

    using var response = await PostAsync(client, ApiRoutes.AuthRegisterOptions, new { code }).ConfigureAwait(true);

    response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    File.Exists(Path.Combine(host.AuthDir, "passkeys.json")).Should().BeFalse();
  }

  [Fact]
  public async Task ExpiredCodeGetsA1() {
    var clock = new TestClock(DateTimeOffset.UtcNow);
    using var host = new AuthTestHost(verifier: new FakePasskeyVerifier(), time: clock);
    using var client = NewClient(host);
    var code = new RegistrationCodeStore(host.AuthDir, clock).NewCode();
    clock.Advance(TimeSpan.FromMinutes(11));

    using var response = await PostAsync(client, ApiRoutes.AuthRegisterOptions, new { code }).ConfigureAwait(true);

    response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
  }

  [Fact]
  public async Task FiveWrongCodesCancelTheCodeAndBlockTheSource() {
    using var host = new AuthTestHost(verifier: new FakePasskeyVerifier());
    using var client = NewClient(host);
    var code = new RegistrationCodeStore(host.AuthDir, TimeProvider.System).NewCode();

    for (var i = 0; i < 5; i++) {
      using var wrong = await PostAsync(client, ApiRoutes.AuthRegisterOptions, new { code = "AAAA-AAAA" }, "203.0.113.50").ConfigureAwait(true);
      wrong.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    File.Exists(Path.Combine(host.AuthDir, "registration-code.json")).Should().BeFalse();
    using var correct = await PostAsync(client, ApiRoutes.AuthRegisterOptions, new { code }, "203.0.113.51").ConfigureAwait(true);
    correct.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    RecordLines(host).Should().Contain(l => l.Contains("source_blocked", StringComparison.Ordinal));
  }

  [Fact]
  public async Task NoEndpointGivesACode() {
    using var host = new AuthTestHost(verifier: new FakePasskeyVerifier());
    using var client = NewClient(host);
    var code = new RegistrationCodeStore(host.AuthDir, TimeProvider.System).NewCode();
    var cookie = await RegisterAsync(host, client, "BAUG").ConfigureAwait(true);

    foreach (var path in new[] { "/api/auth/new-code", "/api/auth/register/code", "/api/auth/codes" }) {
      using var response = await PostAsync(client, path, null, cookie: cookie).ConfigureAwait(true);
      var text = await response.Content.ReadAsStringAsync().ConfigureAwait(true);
      text.Should().NotContain(code.Replace("-", string.Empty, StringComparison.Ordinal));
    }
  }
}

public sealed class SignInFlowTests {
  [Fact]
  public async Task OptionsAreTheSameWithZeroPasskeysAndHaveNoCredentialList() {
    using var host = new AuthTestHost(verifier: new FakePasskeyVerifier());
    using var client = NewClient(host);

    using var response = await PostAsync(client, ApiRoutes.AuthSignInOptions, null).ConfigureAwait(true);

    response.StatusCode.Should().Be(HttpStatusCode.OK);
    var json = await ReadJsonAsync(response).ConfigureAwait(true);
    json.GetProperty("publicKey").GetProperty("allowCredentials").GetArrayLength().Should().Be(0);
  }

  [Fact]
  public async Task RegisteredPasskeySignsIn() {
    using var host = new AuthTestHost(verifier: new FakePasskeyVerifier());
    using var client = NewClient(host);
    await RegisterAsync(host, client).ConfigureAwait(true);

    using var response = await SignInAsync(client).ConfigureAwait(true);

    response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    var cookie = CookieOf(response);
    cookie.Should().NotBeNull();
    var passkeys = JsonDocument.Parse(File.ReadAllText(Path.Combine(host.AuthDir, "passkeys.json")));
    var item = passkeys.RootElement.GetProperty("passkeys")[0];
    item.GetProperty("signCount").GetUInt32().Should().Be(1);
    item.GetProperty("lastUsedUtc").ValueKind.Should().NotBe(JsonValueKind.Null);
    RecordLines(host).Should().Contain(l => l.Contains("\"sign_in\"", StringComparison.Ordinal));
    (await GetAsync(client, ProbePath, "8.8.8.8", "bot.example.com", cookie: cookie).ConfigureAwait(true)).Status
      .Should().Be(HttpStatusCode.OK);
  }

  [Fact]
  public async Task PasskeyThatIsNotRegisteredGetsA1AndARecord() {
    using var host = new AuthTestHost(verifier: new FakePasskeyVerifier());
    using var client = NewClient(host);

    using var response = await SignInAsync(client, "BAUG").ConfigureAwait(true);

    response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    RecordLines(host).Should().Contain(l => l.Contains("sign_in_failed", StringComparison.Ordinal));
  }

  [Fact]
  public async Task InvalidSignatureUnknownCeremonyAndExpiredCeremonyGetA1() {
    var clock = new TestClock(DateTimeOffset.UtcNow);
    using var host = new AuthTestHost(verifier: new FakePasskeyVerifier(), time: clock);
    using var client = NewClient(host);
    await RegisterAsync(host, client).ConfigureAwait(true);

    using var invalid = await SignInAsync(client, valid: false).ConfigureAwait(true);
    invalid.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

    using var unknown = await PostAsync(client, ApiRoutes.AuthSignInComplete, new {
      ceremonyId = "nope",
      credential = new { id = "AQID", rawId = "AQID", valid = true }
    }).ConfigureAwait(true);
    unknown.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

    using var options = await PostAsync(client, ApiRoutes.AuthSignInOptions, null).ConfigureAwait(true);
    var ceremony = (await ReadJsonAsync(options).ConfigureAwait(true)).GetProperty("ceremonyId").GetString();
    clock.Advance(TimeSpan.FromMinutes(6));
    using var expired = await PostAsync(client, ApiRoutes.AuthSignInComplete, new {
      ceremonyId = ceremony,
      credential = new { id = "AQID", rawId = "AQID", valid = true }
    }).ConfigureAwait(true);
    expired.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
  }

  [Fact]
  public async Task FiveFailuresBlockTheSource() {
    using var host = new AuthTestHost(verifier: new FakePasskeyVerifier());
    using var client = NewClient(host);
    await RegisterAsync(host, client).ConfigureAwait(true);

    for (var i = 0; i < 5; i++) {
      using var bad = await SignInAsync(client, valid: false, remote: "203.0.113.60").ConfigureAwait(true);
      bad.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    using var blocked = await PostAsync(client, ApiRoutes.AuthSignInOptions, null, "203.0.113.60").ConfigureAwait(true);
    blocked.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    using var other = await SignInAsync(client, remote: "203.0.113.61").ConfigureAwait(true);
    other.StatusCode.Should().Be(HttpStatusCode.NoContent);
  }
}

public sealed class SessionGateTests {
  [Fact]
  public async Task SessionCookieAttributesFollowTheContract() {
    using var host = new AuthTestHost(verifier: new FakePasskeyVerifier());
    using var client = NewClient(host);
    await RegisterAsync(host, client).ConfigureAwait(true);

    using var response = await SignInAsync(client).ConfigureAwait(true);

    var line = SetCookieLine(response)!;
    line.Should().StartWith("__Host-gamebot-session=");
    line.Should().Contain("secure").And.Contain("httponly").And.Contain("samesite=strict").And.Contain("path=/").And.Contain("max-age=2592000");
  }

  [Fact]
  public async Task SessionPassesForGetAndPostAndStatusShowsSignedIn() {
    using var host = new AuthTestHost(verifier: new FakePasskeyVerifier());
    using var client = NewClient(host);
    var cookie = await RegisterAsync(host, client).ConfigureAwait(true);

    (await GetAsync(client, ProbePath, "8.8.8.8", "bot.example.com", cookie: cookie).ConfigureAwait(true)).Status
      .Should().Be(HttpStatusCode.OK);
    using var post = await PostAsync(client, ApiRoutes.AuthSignOut, null, "8.8.8.8", cookie: cookie).ConfigureAwait(true);
    post.StatusCode.Should().Be(HttpStatusCode.NoContent);

    var again = await RegisterAsync(host, client, "BAUG").ConfigureAwait(true);
    var (_, body) = await GetAsync(client, "/api/auth/status", "8.8.8.8", "bot.example.com", cookie: again).ConfigureAwait(true);
    JsonDocument.Parse(body).RootElement.GetProperty("signedIn").GetBoolean().Should().BeTrue();
  }

  [Fact]
  public async Task ForeignOriginOnAPostGetsA1ButPublicOriginAndNoOriginPass() {
    using var host = new AuthTestHost(verifier: new FakePasskeyVerifier());
    using var client = NewClient(host);
    var cookie = await RegisterAsync(host, client).ConfigureAwait(true);

    using var foreign = await PostAsync(client, ApiRoutes.AuthSignOut, null, cookie: cookie, origin: "https://evil.example.org").ConfigureAwait(true);
    foreign.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

    using var same = await PostAsync(client, ApiRoutes.AuthSignOut, null, cookie: cookie, origin: AuthTestConstants.PublicOrigin).ConfigureAwait(true);
    same.StatusCode.Should().Be(HttpStatusCode.NoContent);

    var second = await RegisterAsync(host, client, "BAUG").ConfigureAwait(true);
    using var none = await PostAsync(client, ApiRoutes.AuthSignOut, null, cookie: second).ConfigureAwait(true);
    none.StatusCode.Should().Be(HttpStatusCode.NoContent);
  }

  [Fact]
  public async Task UnknownCookieGetsA1() {
    using var host = new AuthTestHost(verifier: new FakePasskeyVerifier());
    using var client = NewClient(host);

    (await GetAsync(client, ProbePath, cookie: SessionStore.CookieName + "=bogus").ConfigureAwait(true)).Status
      .Should().Be(HttpStatusCode.Unauthorized);
  }
}

public sealed class SessionLifeTests {
  [Fact]
  public async Task SessionSurvivesAHostRestartOnTheSameDataDirectory() {
    var dir = Path.Combine(Path.GetTempPath(), "gamebot-auth-" + Guid.NewGuid().ToString("N"));
    string cookie;
    using (var first = new AuthTestHost(verifier: new FakePasskeyVerifier(), dataDir: dir)) {
      using var client = NewClient(first);
      cookie = await RegisterAsync(first, client).ConfigureAwait(true);
    }

    try {
      using var second = new AuthTestHost(verifier: new FakePasskeyVerifier(), dataDir: dir);
      using var client = NewClient(second);
      (await GetAsync(client, ProbePath, cookie: cookie).ConfigureAwait(true)).Status.Should().Be(HttpStatusCode.OK);
    }
    finally {
      if (Directory.Exists(dir)) {
        Directory.Delete(dir, recursive: true);
      }
    }
  }

  [Fact]
  public async Task UseOnDay29KeepsTheSessionValidOnDay40AndNoUseExpiresIt() {
    var clock = new TestClock(new DateTimeOffset(2026, 11, 1, 12, 0, 0, TimeSpan.Zero));
    using var host = new AuthTestHost(verifier: new FakePasskeyVerifier(), time: clock);
    using var client = NewClient(host);
    var cookie = await RegisterAsync(host, client).ConfigureAwait(true);

    clock.Advance(TimeSpan.FromDays(29));
    (await GetAsync(client, ProbePath, cookie: cookie).ConfigureAwait(true)).Status.Should().Be(HttpStatusCode.OK);
    clock.Advance(TimeSpan.FromDays(11));
    (await GetAsync(client, ProbePath, cookie: cookie).ConfigureAwait(true)).Status.Should().Be(HttpStatusCode.OK);
    clock.Advance(TimeSpan.FromDays(31));
    (await GetAsync(client, ProbePath, cookie: cookie).ConfigureAwait(true)).Status.Should().Be(HttpStatusCode.Unauthorized);
  }

  [Fact]
  public async Task ResponseRefreshesTheCookieAtMostOneTimeInEachHour() {
    var clock = new TestClock(new DateTimeOffset(2026, 11, 1, 12, 0, 0, TimeSpan.Zero));
    using var host = new AuthTestHost(verifier: new FakePasskeyVerifier(), time: clock);
    using var client = NewClient(host);
    var cookie = await RegisterAsync(host, client).ConfigureAwait(true);

    using var early = AuthTestHost.Request(HttpMethod.Get, ProbePath);
    early.Headers.TryAddWithoutValidation("Cookie", cookie);
    using var earlyResponse = await client.SendAsync(early).ConfigureAwait(true);
    SetCookieLine(earlyResponse).Should().BeNull();

    clock.Advance(TimeSpan.FromHours(2));
    using var later = AuthTestHost.Request(HttpMethod.Get, ProbePath);
    later.Headers.TryAddWithoutValidation("Cookie", cookie);
    using var laterResponse = await client.SendAsync(later).ConfigureAwait(true);
    SetCookieLine(laterResponse).Should().NotBeNull();

    using var again = AuthTestHost.Request(HttpMethod.Get, ProbePath);
    again.Headers.TryAddWithoutValidation("Cookie", cookie);
    using var againResponse = await client.SendAsync(again).ConfigureAwait(true);
    SetCookieLine(againResponse).Should().BeNull();
  }
}

/// <summary>A clock that a test can move.</summary>
internal sealed class TestClock : TimeProvider {
  private DateTimeOffset _now;

  public TestClock(DateTimeOffset start) {
    _now = start;
  }

  public override DateTimeOffset GetUtcNow() => _now;

  public void Advance(TimeSpan delta) => _now += delta;
}
