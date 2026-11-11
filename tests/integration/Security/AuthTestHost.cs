using System.Net;
using System.Text.Json;
using GameBot.Service.Security;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace GameBot.IntegrationTests.Security;

/// <summary>The default public host of the tests.</summary>
internal static class AuthTestConstants {
  public const string PublicHost = "bot.example.com";
  public const string PublicOrigin = "https://bot.example.com";
  public const string Token = "owner-token";
  public const string RemoteHeader = "X-Test-Remote-Ip";
}

/// <summary>
/// A test host for the sign-in function (feature 187). It sets the auth settings, uses a temporary data directory,
/// sets the remote address of a request (from the header <see cref="AuthTestConstants.RemoteHeader"/>), and can
/// replace <see cref="IPasskeyVerifier"/> and the clock.
/// </summary>
internal sealed class AuthTestHost : WebApplicationFactory<Program> {
  private readonly Dictionary<string, string?> _settings = new();
  private readonly IPasskeyVerifier? _verifier;
  private readonly TimeProvider? _time;
  private readonly bool _ownsDataDir;

  public AuthTestHost(
    bool enabled = true,
    string? publicOrigin = AuthTestConstants.PublicOrigin,
    string? trustedProxies = null,
    string? token = AuthTestConstants.Token,
    IPasskeyVerifier? verifier = null,
    TimeProvider? time = null,
    string? dataDir = null) {
    _ownsDataDir = dataDir is null;
    DataDir = dataDir ?? Path.Combine(Path.GetTempPath(), "gamebot-auth-" + Guid.NewGuid().ToString("N"));
    _verifier = verifier;
    _time = time;
    _settings["Service:Storage:Root"] = DataDir;
    _settings["Service:Auth:Enabled"] = enabled ? "true" : "false";
    _settings["Service:Auth:PublicOrigin"] = publicOrigin;
    _settings["Service:Auth:TrustedProxies"] = trustedProxies;
    _settings["Service:Auth:Token"] = token ?? string.Empty;
  }

  /// <summary>The data directory of this host.</summary>
  public string DataDir { get; }

  /// <summary>The auth directory below the data directory.</summary>
  public string AuthDir => Path.Combine(DataDir, "auth");

  protected override void ConfigureWebHost(IWebHostBuilder builder) {
    foreach (var pair in _settings) {
      builder.UseSetting(pair.Key, pair.Value);
    }

    builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(_settings));
    builder.ConfigureServices(services => {
      services.AddSingleton<IStartupFilter>(new RemoteAddressStartupFilter());
    });
    builder.ConfigureTestServices(services => {
      if (_verifier is not null) {
        services.AddSingleton(_verifier);
      }

      if (_time is not null) {
        services.AddSingleton(_time);
      }
    });
  }

  protected override void Dispose(bool disposing) {
    base.Dispose(disposing);
    if (disposing && _ownsDataDir && Directory.Exists(DataDir)) {
      try {
        Directory.Delete(DataDir, recursive: true);
      }
      catch (IOException) {
        // The host can still hold a file. The temporary directory stays.
      }
    }
  }

  /// <summary>A request with a remote address and an optional host.</summary>
  public static HttpRequestMessage Request(HttpMethod method, string path, string? remote = null, string? host = null, string? bearer = null) {
    var request = new HttpRequestMessage(method, new Uri(path, UriKind.Relative));
    if (remote is not null) {
      request.Headers.TryAddWithoutValidation(AuthTestConstants.RemoteHeader, remote);
    }

    if (host is not null) {
      request.Headers.Host = host;
    }

    if (bearer is not null) {
      request.Headers.TryAddWithoutValidation("Authorization", "Bearer " + bearer);
    }

    return request;
  }

  /// <summary>The headers that Traefik sends by default: forward headers and the public host.</summary>
  public static void ApplyTraefikProfile(HttpRequestMessage request, string clientAddress = "203.0.113.9") {
    ArgumentNullException.ThrowIfNull(request);
    request.Headers.Host = AuthTestConstants.PublicHost;
    request.Headers.TryAddWithoutValidation("X-Forwarded-For", clientAddress);
    request.Headers.TryAddWithoutValidation("X-Forwarded-Proto", "https");
    request.Headers.TryAddWithoutValidation("X-Forwarded-Host", AuthTestConstants.PublicHost);
  }

  /// <summary>The headers that the default nginx proxy_pass sends: no forward header and the host of the service.</summary>
  public static void ApplyNginxProfile(HttpRequestMessage request, string serviceHost = "192.168.1.20:8080") {
    ArgumentNullException.ThrowIfNull(request);
    request.Headers.Host = serviceHost;
  }

  private sealed class RemoteAddressStartupFilter : IStartupFilter {
    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app => {
      app.Use(async (context, nextMiddleware) => {
        if (context.Request.Headers.TryGetValue(AuthTestConstants.RemoteHeader, out var value)
            && IPAddress.TryParse(value.ToString(), out var address)) {
          context.Connection.RemoteIpAddress = address;
          context.Request.Headers.Remove(AuthTestConstants.RemoteHeader);
        }

        await nextMiddleware(context).ConfigureAwait(false);
      });
      next(app);
    };
  }
}

/// <summary>A fake passkey verifier. A credential is valid when its JSON has <c>"valid": true</c>.</summary>
internal sealed class FakePasskeyVerifier : IPasskeyVerifier {
  public PasskeyOptions CreateRegistrationOptions(byte[] userHandle, IReadOnlyList<string> excludeCredentialIds) =>
    new() { PublicKey = JsonSerializer.SerializeToElement(new { challenge = "fake", excludeCredentials = excludeCredentialIds }) };

  public PasskeyOptions CreateSignInOptions() =>
    new() { PublicKey = JsonSerializer.SerializeToElement(new { challenge = "fake", rpId = AuthTestConstants.PublicHost, allowCredentials = Array.Empty<object>() }) };

  public Task<VerifiedRegistration?> VerifyRegistrationAsync(PasskeyOptions options, JsonElement credential, CancellationToken cancellationToken) {
    var valid = IsValid(credential);
    var id = credential.TryGetProperty("rawId", out var raw) ? raw.GetString() ?? "id" : "id";
    return Task.FromResult<VerifiedRegistration?>(valid ? new VerifiedRegistration(id, "fake-public-key", 0) : null);
  }

  public Task<uint?> VerifySignInAsync(
    PasskeyOptions options, JsonElement credential, string storedPublicKey, uint storedSignCount, byte[] userHandle, CancellationToken cancellationToken) =>
    Task.FromResult<uint?>(IsValid(credential) ? storedSignCount + 1 : null);

  private static bool IsValid(JsonElement credential) =>
    credential.ValueKind == JsonValueKind.Object
    && credential.TryGetProperty("valid", out var valid)
    && valid.ValueKind == JsonValueKind.True;
}
