using System.Net;

namespace GameBot.Service.Security;

/// <summary>
/// Gives the source address of a request for the limiter and the record (feature 187, FR-029, research R8).
/// Only a request from a trusted proxy or from loopback may use <c>X-Forwarded-For</c>. The service uses the
/// last entry that is not a trusted proxy. All other requests use the remote address, and a forged header has no effect.
/// </summary>
internal sealed class ClientAddressResolver {
  private const string UnknownSource = "unknown";

  private readonly AuthOptions _options;
  private readonly SignInRecorder _recorder;

  public ClientAddressResolver(AuthOptions options, SignInRecorder recorder) {
    _options = options ?? throw new ArgumentNullException(nameof(options));
    _recorder = recorder ?? throw new ArgumentNullException(nameof(recorder));
  }

  public string Resolve(HttpContext context) {
    ArgumentNullException.ThrowIfNull(context);
    var remote = context.Connection.RemoteIpAddress;
    var remoteText = remote is null ? UnknownSource : AuthOptions.Normalize(remote).ToString();
    var fromGateway = remote is not null && (_options.IsTrustedProxy(remote) || IPAddress.IsLoopback(AuthOptions.Normalize(remote)));
    if (!fromGateway) {
      return remoteText;
    }

    var header = context.Request.Headers["X-Forwarded-For"].ToString();
    if (string.IsNullOrWhiteSpace(header)) {
      return NoClientAddress(remote, remoteText);
    }

    var entries = header.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    for (var i = entries.Length - 1; i >= 0; i--) {
      if (!IPAddress.TryParse(entries[i], out var address)) {
        continue;
      }

      if (_options.IsTrustedProxy(address)) {
        continue;
      }

      return AuthOptions.Normalize(address).ToString();
    }

    return NoClientAddress(remote, remoteText);
  }

  private string NoClientAddress(IPAddress? remote, string remoteText) {
    // A plain loopback request with no header comes from the service machine. It is not a proxy problem.
    if (remote is not null && _options.IsTrustedProxy(remote)) {
      _recorder.RecordProxyNoClientAddress(remoteText);
    }

    return remoteText;
  }
}
