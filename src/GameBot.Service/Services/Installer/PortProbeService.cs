using System.Net;
using System.Net.Sockets;
using GameBot.Domain.Installer;

namespace GameBot.Service.Services.Installer;

internal sealed class PortProbeService {
  private readonly Func<int, bool> _isPortAvailable;

  public PortProbeService() : this(IsPortAvailable) {
  }

  internal PortProbeService(Func<int, bool> isPortAvailable) {
    _isPortAvailable = isPortAvailable ?? throw new ArgumentNullException(nameof(isPortAvailable));
  }

  public int ResolvePreferredWebUiPort(int backendPort, int? requestedWebUiPort) {
    if (requestedWebUiPort.HasValue && requestedWebUiPort.Value != backendPort && _isPortAvailable(requestedWebUiPort.Value)) {
      return requestedWebUiPort.Value;
    }

    foreach (var candidate in InstallerDefaults.PreferredWebPorts) {
      if (candidate == backendPort) {
        continue;
      }

      if (_isPortAvailable(candidate)) {
        return candidate;
      }
    }

    return 0;
  }

  public PortProbeResult Probe(int backendPort, int requestedWebUiPort) {
    var backendPortAvailable = _isPortAvailable(backendPort);
    var webUiPortAvailable = requestedWebUiPort != backendPort && _isPortAvailable(requestedWebUiPort);

    var backendAlternatives = backendPortAvailable
      ? Array.Empty<int>()
      : SuggestBackendAlternatives(backendPort, requestedWebUiPort);

    var webUiAlternatives = webUiPortAvailable
      ? Array.Empty<int>()
      : SuggestWebUiAlternatives(backendPort, requestedWebUiPort);

    return new PortProbeResult {
      RequestedBackendPort = backendPort,
      RequestedWebUiPort = requestedWebUiPort,
      BackendPortAvailable = backendPortAvailable,
      WebUiPortAvailable = webUiPortAvailable,
      BackendAlternatives = backendAlternatives,
      WebUiAlternatives = webUiAlternatives,
      CapturedAtUtc = DateTimeOffset.UtcNow
    };
  }

  private int[] SuggestWebUiAlternatives(int backendPort, int requestedWebUiPort) {
    var alternatives = new List<int>();
    foreach (var candidate in InstallerDefaults.PreferredWebPorts) {
      if (candidate == requestedWebUiPort || candidate == backendPort) {
        continue;
      }

      if (_isPortAvailable(candidate)) {
        alternatives.Add(candidate);
      }
    }

    return [.. alternatives];
  }

  private int[] SuggestBackendAlternatives(int backendPort, int requestedWebUiPort) {
    var alternatives = new List<int>(capacity: 5);
    for (var candidate = backendPort + 1; candidate <= 65535 && alternatives.Count < 5; candidate++) {
      if (candidate == requestedWebUiPort) {
        continue;
      }

      if (_isPortAvailable(candidate)) {
        alternatives.Add(candidate);
      }
    }

    return [.. alternatives];
  }

  private static bool IsPortAvailable(int port) {
    if (port < 1 || port > 65535) {
      return false;
    }

    try {
      using var listener = new TcpListener(IPAddress.Loopback, port);
      listener.Start();
      listener.Stop();
      return true;
    }
    catch (SocketException) {
      return false;
    }
  }
}
