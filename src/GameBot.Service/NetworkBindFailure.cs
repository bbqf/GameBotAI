using System.Net.Sockets;
using Microsoft.Extensions.Logging;

namespace GameBot.Service;

/// <summary>
/// Helps with a failed bind of the web server (feature 162, FR-022). A silent upgrade keeps a saved port even when
/// another program holds it. The bot then cannot start its web server, so no status page can show the failure.
/// The bot writes the failure to its log and ends with a non-zero exit code.
/// </summary>
internal static class NetworkBindFailure {
  /// <summary>The exit code of the bot when the listen address is in use (the Linux code for "address in use").</summary>
  public const int ExitCode = 98;

  /// <summary>True when the exception, or an exception inside it, says that the address is already in use.</summary>
  public static bool IsAddressInUse(Exception exception) {
    ArgumentNullException.ThrowIfNull(exception);
    for (var current = exception; current is not null; current = current.InnerException) {
      if (current is SocketException { SocketErrorCode: SocketError.AddressAlreadyInUse }) {
        return true;
      }

      if (current.Message.Contains("address already in use", StringComparison.OrdinalIgnoreCase)) {
        return true;
      }
    }

    return false;
  }

  private static readonly Action<ILogger, string, Exception?> s_logBindFailure =
    LoggerMessage.Define<string>(LogLevel.Error, new EventId(2, "NetworkBindFailure"), "{Message}");

  /// <summary>Writes the failure to the log as an error.</summary>
  public static void Log(ILogger logger, Exception exception, string message) {
    ArgumentNullException.ThrowIfNull(logger);
    s_logBindFailure(logger, message, exception);
  }

  /// <summary>The log text. It names the host, the port, and where each value came from.</summary>
  public static string Describe(GameBotServiceSetup.ListenAddress address) {
    ArgumentNullException.ThrowIfNull(address);
    return $"The bot cannot listen on {address.Host}:{address.Port} because the address is in use. "
           + $"The host comes from {address.HostSource}. The port comes from {address.PortSource}. "
           + "Stop the other program that uses this port, or change the port with the installer or in network.json.";
  }
}
