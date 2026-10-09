using System.Net;

namespace GameBot.Service.Services.Updates;

/// <summary>
/// Tells if a request comes from the bot PC (FR-014, research R-006). The check uses the address of the
/// real socket. It never reads a header such as X-Forwarded-For, because a client can fake a header.
/// <see cref="IPAddress.IsLoopback"/> also covers an IPv4 address that is mapped into IPv6.
/// </summary>
internal static class LoopbackGuard {
  public static bool IsLocalRequest(HttpContext context) {
    ArgumentNullException.ThrowIfNull(context);
    return IsLoopback(context.Connection.RemoteIpAddress);
  }

  public static bool IsLoopback(IPAddress? remoteAddress) =>
    remoteAddress is not null && IPAddress.IsLoopback(remoteAddress);
}
