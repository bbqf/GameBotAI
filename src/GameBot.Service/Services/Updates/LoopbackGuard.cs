using System.Net;

namespace GameBot.Service.Services.Updates;

/// <summary>
/// Tells if a request comes from the bot PC (FR-014, research R-006). The check uses the addresses of the
/// real socket. It never reads a header such as X-Forwarded-For or Host, because a client can fake a header.
/// A request is local when the client address is a loopback address, or when it is the same as the
/// address of the service end of the socket. The second rule covers a browser on the bot PC that opens
/// the service by its network address, for example when the service listens on 192.168.1.5 and not on localhost.
/// <see cref="IPAddress.IsLoopback"/> also covers an IPv4 address that is mapped into IPv6.
/// </summary>
internal static class LoopbackGuard {
  public static bool IsLocalRequest(HttpContext context) {
    ArgumentNullException.ThrowIfNull(context);
    return IsLocal(context.Connection.RemoteIpAddress, context.Connection.LocalIpAddress);
  }

  public static bool IsLoopback(IPAddress? remoteAddress) =>
    remoteAddress is not null && IPAddress.IsLoopback(remoteAddress);

  public static bool IsLocal(IPAddress? remoteAddress, IPAddress? localAddress) {
    if (remoteAddress is null) {
      return false;
    }

    return IPAddress.IsLoopback(remoteAddress)
      || (localAddress is not null && Normalize(remoteAddress).Equals(Normalize(localAddress)));
  }

  private static IPAddress Normalize(IPAddress address) => address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address;
}
