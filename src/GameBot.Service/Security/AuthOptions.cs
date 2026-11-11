using System.Globalization;
using System.Net;

namespace GameBot.Service.Security;

/// <summary>
/// Settings of the sign-in function (feature 187). The settings come only from the service
/// configuration and the environment. No endpoint changes them.
/// </summary>
internal sealed class AuthOptions {
  private static readonly char[] Separators = { ',', ';' };
  private readonly List<IPNetwork> _trustedNetworks = new();
  private readonly List<string> _problems = new();

  private AuthOptions() { }

  /// <summary>True when the sign-in function is on. The default is false.</summary>
  public bool Enabled { get; private set; }

  /// <summary>The public https origin, or null when it is absent or not valid.</summary>
  public Uri? PublicOrigin { get; private set; }

  /// <summary>The relying party id: the host of the public origin.</summary>
  public string? RpId => PublicOrigin?.Host;

  /// <summary>The access token, or null when none is configured.</summary>
  public string? Token { get; private set; }

  /// <summary>True when the public origin is present and uses https.</summary>
  public bool IsValid => PublicOrigin is not null;

  /// <summary>The number of trusted proxy entries that the service accepted.</summary>
  public int TrustedProxyCount => _trustedNetworks.Count;

  /// <summary>Problem texts for settings that the service ignored. No secret is in these texts.</summary>
  public IReadOnlyList<string> Problems => _problems;

  /// <summary>The accepted trusted proxy entries as text, for the startup log.</summary>
  public IReadOnlyList<string> TrustedProxyEntries => _trustedNetworks
    .Select(n => n.ToString())
    .ToList();

  /// <summary>Reads the settings from the configuration and from the environment.</summary>
  public static AuthOptions Load(IConfiguration configuration) {
    ArgumentNullException.ThrowIfNull(configuration);
    var enabled = configuration["Service:Auth:Enabled"]
                  ?? Environment.GetEnvironmentVariable("GAMEBOT_AUTH_ENABLED");
    var origin = configuration["Service:Auth:PublicOrigin"]
                 ?? Environment.GetEnvironmentVariable("GAMEBOT_AUTH_PUBLIC_ORIGIN");
    var proxies = configuration["Service:Auth:TrustedProxies"]
                  ?? Environment.GetEnvironmentVariable("GAMEBOT_AUTH_TRUSTED_PROXIES");
    var token = configuration["Service:Auth:Token"]
                ?? Environment.GetEnvironmentVariable("GAMEBOT_AUTH_TOKEN");
    return Create(enabled, origin, proxies, token);
  }

  /// <summary>Makes the settings from raw text values.</summary>
  public static AuthOptions Create(string? enabled, string? publicOrigin, string? trustedProxies, string? token) {
    var options = new AuthOptions {
      Enabled = ParseBool(enabled),
      Token = string.IsNullOrWhiteSpace(token) ? null : token
    };
    options.ParseOrigin(publicOrigin);
    options.ParseProxies(trustedProxies);
    return options;
  }

  /// <summary>True when the address is in the list of trusted proxies.</summary>
  public bool IsTrustedProxy(IPAddress? address) {
    if (address is null || _trustedNetworks.Count == 0) {
      return false;
    }

    var normal = Normalize(address);
    return _trustedNetworks.Any(n => n.Contains(normal));
  }

  internal static IPAddress Normalize(IPAddress address) =>
    address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address;

  private static bool ParseBool(string? value) =>
    !string.IsNullOrWhiteSpace(value)
    && (value.Trim().Equals("true", StringComparison.OrdinalIgnoreCase) || value.Trim() == "1");

  private void ParseOrigin(string? raw) {
    if (string.IsNullOrWhiteSpace(raw)) {
      if (Enabled) {
        _problems.Add("The public origin is not set. Set GAMEBOT_AUTH_PUBLIC_ORIGIN to an https address.");
      }

      return;
    }

    if (!Uri.TryCreate(raw.Trim(), UriKind.Absolute, out var uri)
        || !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.Ordinal)
        || string.IsNullOrEmpty(uri.Host)) {
      _problems.Add("The public origin is not valid. It must be an https address.");
      return;
    }

    PublicOrigin = new Uri(uri.GetLeftPart(UriPartial.Authority), UriKind.Absolute);
  }

  private void ParseProxies(string? raw) {
    if (string.IsNullOrWhiteSpace(raw)) {
      return;
    }

    foreach (var entry in raw.Split(Separators,StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)) {
      if (TryParseEntry(entry, out var network)) {
        _trustedNetworks.Add(network);
      }
      else {
        _problems.Add(string.Create(CultureInfo.InvariantCulture, $"The trusted proxy entry '{entry}' is not valid. The service ignores it."));
      }
    }
  }

  private static bool TryParseEntry(string entry, out IPNetwork network) {
    network = default;
    if (entry.Contains('/', StringComparison.Ordinal)) {
      if (!IPNetwork.TryParse(entry, out var parsed)) {
        return false;
      }

      network = parsed;
      return true;
    }

    if (!IPAddress.TryParse(entry, out var address)) {
      return false;
    }

    var normal = Normalize(address);
    var prefix = normal.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork ? 32 : 128;
    network = new IPNetwork(normal, prefix);
    return true;
  }
}
