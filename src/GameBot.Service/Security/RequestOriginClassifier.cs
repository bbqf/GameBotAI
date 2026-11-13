using System.Net;
using System.Net.Sockets;

namespace GameBot.Service.Security;

/// <summary>
/// Gives the source of a request: the service machine, the home network, or the internet.
/// The rules apply in order and the first match gives the result (research R6).
/// </summary>
internal sealed class RequestOriginClassifier {
  private static readonly string[] ForwardHeaders = {
    "Forwarded", "X-Forwarded-For", "X-Forwarded-Host", "X-Forwarded-Proto",
    "X-Real-IP", "CF-Connecting-IP", "True-Client-IP", "Via"
  };

  private readonly AuthOptions _options;

  public RequestOriginClassifier(AuthOptions options) {
    _options = options ?? throw new ArgumentNullException(nameof(options));
  }

  public RequestOrigin Classify(HttpContext context) {
    ArgumentNullException.ThrowIfNull(context);
    var remote = context.Connection.RemoteIpAddress;

    // Rule 1: a configured proxy always gives an internet request.
    if (_options.IsTrustedProxy(remote)) {
      return RequestOrigin.Internet;
    }

    // Rule 2: a forward header only lowers the trust. The service never reads its value for access.
    if (HasForwardHeader(context.Request.Headers)) {
      return RequestOrigin.Internet;
    }

    var host = context.Request.Host.Host;
    var loopback = remote is null || IPAddress.IsLoopback(remote);

    // Rule 3: loopback address and a loopback host name.
    if (loopback && IsLoopbackHost(host)) {
      return RequestOrigin.LocalMachine;
    }

    // Rule 4: the sign-in function is on and no trusted proxy is set. The service cannot tell a request
    // that a proxy sent with no forward header from a request of a home network device. The client
    // controls the Host header, so the header must not raise the trust. Only loopback stays inside.
    if (_options.Enabled && _options.TrustedProxyCount == 0 && !loopback) {
      return RequestOrigin.Internet;
    }

    // Rule 5: private or loopback address, and the host is not the public host.
    if ((loopback || IsPrivate(remote!)) && !IsPublicHost(host)) {
      return RequestOrigin.HomeNetwork;
    }

    // Rule 6: a doubt gives Internet.
    return RequestOrigin.Internet;
  }

  internal static bool HasForwardHeader(IHeaderDictionary headers) {
    foreach (var name in ForwardHeaders) {
      if (headers.ContainsKey(name)) {
        return true;
      }
    }

    return false;
  }

  private static bool IsLoopbackHost(string host) =>
    host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
    || (IPAddress.TryParse(host.Trim('[', ']'), out var address) && IPAddress.IsLoopback(address));

  private bool IsPublicHost(string host) =>
    _options.PublicOrigin is not null
    && host.Equals(_options.PublicOrigin.Host, StringComparison.OrdinalIgnoreCase);

  internal static bool IsPrivate(IPAddress address) {
    var normal = AuthOptions.Normalize(address);
    if (normal.AddressFamily == AddressFamily.InterNetwork) {
      var b = normal.GetAddressBytes();
      return b[0] == 10
             || (b[0] == 172 && b[1] >= 16 && b[1] <= 31)
             || (b[0] == 192 && b[1] == 168)
             || (b[0] == 169 && b[1] == 254);
    }

    return normal.AddressFamily == AddressFamily.InterNetworkV6
           && (normal.IsIPv6LinkLocal || normal.IsIPv6UniqueLocal);
  }
}
