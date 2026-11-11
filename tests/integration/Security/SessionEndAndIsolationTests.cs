using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using GameBot.Service;
using GameBot.Service.Security;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using static GameBot.IntegrationTests.Security.AuthTestFlows;

namespace GameBot.IntegrationTests.Security;

public sealed class SessionEndTests {
  private const string Remote = "203.0.113.70";

  private static async Task<(string Cookie, string PasskeyId)> SignedInDeviceAsync(AuthTestHost host, HttpClient client, string credentialId) {
    await RegisterAsync(host, client, credentialId, credentialId, Remote).ConfigureAwait(false);
    using var response = await SignInAsync(client, credentialId, remote: Remote).ConfigureAwait(false);
    var passkeys = host.Services.GetRequiredService<PasskeyStore>().List();
    return (CookieOf(response)!, passkeys.Single(p => p.CredentialId == credentialId).Id);
  }

  private static async Task<HttpStatusCode> StatusAsync(HttpClient client, string cookie) =>
    (await GetAsync(client, ProbePath, Remote, AuthTestConstants.PublicHost, cookie: cookie).ConfigureAwait(false)).Status;

  [Fact]
  public async Task SignOutEndsOnlyThisSessionAndClearsTheCookie() {
    using var host = new AuthTestHost(verifier: new FakePasskeyVerifier());
    using var client = NewClient(host);
    var (first, _) = await SignedInDeviceAsync(host, client, "AQID").ConfigureAwait(true);
    using var secondResponse = await SignInAsync(client, "AQID", remote: Remote).ConfigureAwait(true);
    var second = CookieOf(secondResponse)!;

    using var signOut = await PostAsync(client, ApiRoutes.AuthSignOut, null, Remote, cookie: first).ConfigureAwait(true);

    signOut.StatusCode.Should().Be(HttpStatusCode.NoContent);
    SetCookieLine(signOut).Should().Contain("expires=");
    (await StatusAsync(client, first).ConfigureAwait(true)).Should().Be(HttpStatusCode.Unauthorized);
    (await StatusAsync(client, second).ConfigureAwait(true)).Should().Be(HttpStatusCode.OK);
    RecordLines(host).Should().Contain(l => l.Contains("sign_out", StringComparison.Ordinal));
  }

  [Fact]
  public async Task EndAllEndsAllSessions() {
    using var host = new AuthTestHost(verifier: new FakePasskeyVerifier());
    using var client = NewClient(host);
    var (first, _) = await SignedInDeviceAsync(host, client, "AQID").ConfigureAwait(true);
    using var secondResponse = await SignInAsync(client, "AQID", remote: Remote).ConfigureAwait(true);
    var second = CookieOf(secondResponse)!;

    using var endAll = await PostAsync(client, ApiRoutes.AuthSessionsEndAll, null, Remote, cookie: first).ConfigureAwait(true);

    endAll.StatusCode.Should().Be(HttpStatusCode.NoContent);
    (await StatusAsync(client, first).ConfigureAwait(true)).Should().Be(HttpStatusCode.Unauthorized);
    (await StatusAsync(client, second).ConfigureAwait(true)).Should().Be(HttpStatusCode.Unauthorized);
    RecordLines(host).Should().Contain(l => l.Contains("sessions_ended", StringComparison.Ordinal));
  }

  [Fact]
  public async Task ListShowsCurrentAndNoKeyMaterial() {
    using var host = new AuthTestHost(verifier: new FakePasskeyVerifier());
    using var client = NewClient(host);
    var (cookie, id) = await SignedInDeviceAsync(host, client, "AQID").ConfigureAwait(true);

    var (status, body) = await GetAsync(client, ApiRoutes.AuthPasskeys, Remote, AuthTestConstants.PublicHost, cookie: cookie).ConfigureAwait(true);

    status.Should().Be(HttpStatusCode.OK);
    body.Should().NotContain("fake-public-key").And.NotContain("credentialId").And.NotContain("publicKey");
    var item = JsonDocument.Parse(body).RootElement.GetProperty("passkeys")[0];
    item.GetProperty("id").GetString().Should().Be(id);
    item.GetProperty("current").GetBoolean().Should().BeTrue();
    item.GetProperty("name").GetString().Should().Be("AQID");
  }

  [Fact]
  public async Task ListWithATokenAndNoSessionShowsNoCurrentPasskey() {
    using var host = new AuthTestHost(verifier: new FakePasskeyVerifier());
    using var client = NewClient(host);
    await SignedInDeviceAsync(host, client, "AQID").ConfigureAwait(true);

    var (status, body) = await GetAsync(client, ApiRoutes.AuthPasskeys, bearer: AuthTestConstants.Token).ConfigureAwait(true);

    status.Should().Be(HttpStatusCode.OK);
    JsonDocument.Parse(body).RootElement.GetProperty("passkeys")[0].GetProperty("current").GetBoolean().Should().BeFalse();
  }

  [Fact]
  public async Task RenameChangesTheNameAndRejectsANameThatIsNotValid() {
    using var host = new AuthTestHost(verifier: new FakePasskeyVerifier());
    using var client = NewClient(host);
    var (cookie, id) = await SignedInDeviceAsync(host, client, "AQID").ConfigureAwait(true);

    async Task<HttpResponseMessage> Patch(string path, object body) {
      using var request = AuthTestHost.Request(HttpMethod.Patch, path, Remote, AuthTestConstants.PublicHost);
      request.Headers.TryAddWithoutValidation("Cookie", cookie);
      request.Content = JsonContent.Create(body);
      return await client.SendAsync(request).ConfigureAwait(false);
    }

    using var ok = await Patch(ApiRoutes.AuthPasskeys + "/" + id, new { name = "Work laptop" }).ConfigureAwait(true);
    ok.StatusCode.Should().Be(HttpStatusCode.OK);
    (await ReadJsonAsync(ok).ConfigureAwait(true)).GetProperty("name").GetString().Should().Be("Work laptop");

    using var invalid = await Patch(ApiRoutes.AuthPasskeys + "/" + id, new { name = new string('x', 41) }).ConfigureAwait(true);
    invalid.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    (await ReadJsonAsync(invalid).ConfigureAwait(true)).GetProperty("error").GetProperty("code").GetString().Should().Be("invalid_name");

    using var unknown = await Patch(ApiRoutes.AuthPasskeys + "/nope", new { name = "x" }).ConfigureAwait(true);
    unknown.StatusCode.Should().Be(HttpStatusCode.NotFound);
  }

  [Fact]
  public async Task RemoveEndsTheSessionsOfThePasskeyAndTheDeviceCannotSignInAgain() {
    using var host = new AuthTestHost(verifier: new FakePasskeyVerifier());
    using var client = NewClient(host);
    var (cookie, id) = await SignedInDeviceAsync(host, client, "AQID").ConfigureAwait(true);

    using var request = AuthTestHost.Request(HttpMethod.Delete, ApiRoutes.AuthPasskeys + "/" + id, Remote, AuthTestConstants.PublicHost);
    request.Headers.TryAddWithoutValidation("Cookie", cookie);
    using var removed = await client.SendAsync(request).ConfigureAwait(true);

    removed.StatusCode.Should().Be(HttpStatusCode.NoContent);
    SetCookieLine(removed).Should().Contain("expires=");
    (await StatusAsync(client, cookie).ConfigureAwait(true)).Should().Be(HttpStatusCode.Unauthorized);
    using var again = await SignInAsync(client, "AQID", remote: Remote).ConfigureAwait(true);
    again.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    RecordLines(host).Should().Contain(l => l.Contains("passkey_removed", StringComparison.Ordinal));

    using var unknown = AuthTestHost.Request(HttpMethod.Delete, ApiRoutes.AuthPasskeys + "/nope", bearer: AuthTestConstants.Token);
    using var notFound = await client.SendAsync(unknown).ConfigureAwait(true);
    notFound.StatusCode.Should().Be(HttpStatusCode.NotFound);
  }

  [Fact]
  public async Task LocalCommandRemovalTakesEffectInTheRunningServiceWithinAboutOneSecond() {
    using var host = new AuthTestHost(verifier: new FakePasskeyVerifier());
    using var client = NewClient(host);
    var (cookie, id) = await SignedInDeviceAsync(host, client, "AQID").ConfigureAwait(true);
    (await StatusAsync(client, cookie).ConfigureAwait(true)).Should().Be(HttpStatusCode.OK);

    using var output = new StringWriter();
    using var error = new StringWriter();
    var config = new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build();
    AuthCommand.Run(new[] { "auth", "remove", id, "--data-dir", host.DataDir }, output, error, config).Should().Be(0);

    await Task.Delay(1300).ConfigureAwait(true);
    (await StatusAsync(client, cookie).ConfigureAwait(true)).Should().Be(HttpStatusCode.Unauthorized);
  }
}

public sealed class AuthDataIsolationTests {
  private const string Remote = "203.0.113.71";

  [Fact]
  public async Task BackupDoesNotContainTheAuthData() {
    using var host = new AuthTestHost(verifier: new FakePasskeyVerifier());
    using var client = NewClient(host);
    var cookie = await RegisterAsync(host, client, "AQID", "My phone", Remote).ConfigureAwait(true);

    using var create = await PostAsync(client, "/api/commands", new { name = "cmd-for-backup", steps = Array.Empty<object>() }, Remote, AuthTestConstants.PublicHost, cookie).ConfigureAwait(true);
    create.EnsureSuccessStatusCode();
    var commandId = (await ReadJsonAsync(create).ConfigureAwait(true)).GetProperty("id").GetString();
    using var backup = await PostAsync(client, ApiRoutes.AuthoringBackup,
      new { commandIds = new[] { commandId }, sequenceIds = Array.Empty<string>() }, Remote, AuthTestConstants.PublicHost, cookie).ConfigureAwait(true);

    backup.StatusCode.Should().Be(HttpStatusCode.OK);
    var bytes = await backup.Content.ReadAsByteArrayAsync().ConfigureAwait(true);
    using var zip = new ZipArchive(new MemoryStream(bytes));
    foreach (var entry in zip.Entries) {
      entry.FullName.Should().NotContainEquivalentOf("auth").And.NotContain("passkey");
      using var reader = new StreamReader(entry.Open());
      var text = await reader.ReadToEndAsync().ConfigureAwait(true);
      text.Should().NotContain("fake-public-key").And.NotContain("tokenHash");
    }
  }

  [Fact]
  public async Task ConfigFileEndpointsCannotReachTheAuthDirectory() {
    using var host = new AuthTestHost(verifier: new FakePasskeyVerifier());
    using var client = NewClient(host);
    var cookie = await RegisterAsync(host, client, "AQID", "My phone", Remote).ConfigureAwait(true);
    var passkeysBefore = await File.ReadAllTextAsync(Path.Combine(host.AuthDir, "passkeys.json")).ConfigureAwait(true);

    foreach (var name in new[] { "passkeys.json", "..%2Fauth%2Fpasskeys.json", "sessions.json", "..%5Cauth%5Cpasskeys.json" }) {
      var (status, body) = await GetAsync(client, ApiRoutes.ConfigFiles + "/" + name, Remote, AuthTestConstants.PublicHost, cookie: cookie).ConfigureAwait(true);
      status.Should().NotBe(HttpStatusCode.OK);
      body.Should().NotContain("fake-public-key");
    }

    var list = await GetAsync(client, ApiRoutes.ConfigFiles, Remote, AuthTestConstants.PublicHost, cookie: cookie).ConfigureAwait(true);
    list.Body.Should().NotContain("passkeys").And.NotContain("sessions");
    (await File.ReadAllTextAsync(Path.Combine(host.AuthDir, "passkeys.json")).ConfigureAwait(true)).Should().Be(passkeysBefore);
  }
}

public sealed class AuthSettingsIsolationTests {
  private const string Remote = "203.0.113.72";

  private static string[] AuthFiles(AuthTestHost host) =>
    Directory.GetFiles(host.AuthDir).OrderBy(f => f, StringComparer.Ordinal).ToArray();

  [Fact]
  public async Task NoWritePathChangesTheSignInSettings() {
    using var host = new AuthTestHost(verifier: new FakePasskeyVerifier(), trustedProxies: "192.168.1.5");
    using var client = NewClient(host);
    var cookie = await RegisterAsync(host, client, "AQID", "My phone", Remote).ConfigureAwait(true);
    var before = host.Services.GetRequiredService<AuthOptions>();
    var snapshot = (before.Enabled, before.PublicOrigin, before.TrustedProxyCount, before.Token);

    // Parameter update with each sign-in key and the reorder call.
    foreach (var key in new[] { "GAMEBOT_AUTH_ENABLED", "GAMEBOT_AUTH_PUBLIC_ORIGIN", "GAMEBOT_AUTH_TRUSTED_PROXIES" }) {
      using var request = AuthTestHost.Request(HttpMethod.Put, ApiRoutes.Config + "/parameters", Remote, AuthTestConstants.PublicHost);
      request.Headers.TryAddWithoutValidation("Cookie", cookie);
      request.Content = JsonContent.Create(new { updates = new Dictionary<string, string?> { [key] = "false" } });
      using var response = await client.SendAsync(request).ConfigureAwait(true);
      response.StatusCode.Should().NotBe(HttpStatusCode.OK, key);
    }

    // Configuration files that carry the names of the sign-in settings.
    foreach (var name in new[] { "logging-policy.json", "execution-log-policy.json" }) {
      using var request = AuthTestHost.Request(HttpMethod.Put, ApiRoutes.ConfigFiles + "/" + name, Remote, AuthTestConstants.PublicHost);
      request.Headers.TryAddWithoutValidation("Cookie", cookie);
      request.Content = new StringContent("{\"Service\":{\"Auth\":{\"Enabled\":false,\"TrustedProxies\":\"\",\"PublicOrigin\":\"https://evil.example.org\"}}}", System.Text.Encoding.UTF8, "application/json");
      using var response = await client.SendAsync(request).ConfigureAwait(true);
      response.StatusCode.Should().NotBe(HttpStatusCode.InternalServerError);
    }

    // Restore with an archive that names the sign-in files and settings.
    using var archive = new MemoryStream();
    using (var zip = new ZipArchive(archive, ZipArchiveMode.Create, leaveOpen: true)) {
      foreach (var name in new[] { "auth/passkeys.json", "../auth/passkeys.json", "appsettings.json", "cmd_x.json" }) {
        var entry = zip.CreateEntry(name);
        await using var stream = entry.Open();
        await stream.WriteAsync("{\"Service\":{\"Auth\":{\"Enabled\":false}}}"u8.ToArray()).ConfigureAwait(true);
      }
    }

    var filesBefore = AuthFiles(host);
    var passkeysBefore = await File.ReadAllTextAsync(Path.Combine(host.AuthDir, "passkeys.json")).ConfigureAwait(true);
    using var content = new MultipartFormDataContent();
    using var fileContent = new ByteArrayContent(archive.ToArray());
    fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/zip");
    content.Add(fileContent, "archive", "backup.zip");
    using var restore = AuthTestHost.Request(HttpMethod.Post, ApiRoutes.AuthoringRestoreApply, Remote, AuthTestConstants.PublicHost);
    restore.Headers.TryAddWithoutValidation("Cookie", cookie);
    restore.Content = content;
    using var restoreResponse = await client.SendAsync(restore).ConfigureAwait(true);
    restoreResponse.StatusCode.Should().NotBe(HttpStatusCode.InternalServerError);

    // The settings and the files are the same. A new read of the settings gives the same values.
    var after = AuthOptions.Load(host.Services.GetRequiredService<Microsoft.Extensions.Configuration.IConfiguration>());
    (after.Enabled, after.PublicOrigin, after.TrustedProxyCount, after.Token).Should().Be(snapshot);
    AuthFiles(host).Should().Equal(filesBefore);
    (await File.ReadAllTextAsync(Path.Combine(host.AuthDir, "passkeys.json")).ConfigureAwait(true)).Should().Be(passkeysBefore);
    (await GetAsync(client, ApiRoutes.AuthStatus, Remote, AuthTestConstants.PublicHost, cookie: cookie).ConfigureAwait(true)).Body
      .Should().Contain("\"signInEnabled\":true");
  }
}
