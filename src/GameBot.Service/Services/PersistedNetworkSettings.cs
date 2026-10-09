using System.Globalization;
using System.Text.Json;
using Microsoft.Win32;

namespace GameBot.Service.Services;

/// <summary>The saved listen host and port. A null field means "no valid saved value".</summary>
internal sealed record PersistedNetworkValues(string? BindHost, int? Port, IReadOnlyList<string> Problems);

/// <summary>
/// Reads the saved listen host and port: first <c>&lt;data root&gt;\config\network.json</c>, then the
/// older registry values. Each field is checked alone. This class never throws and never writes.
/// </summary>
internal static class PersistedNetworkSettings {
  public const string FileName = "network.json";
  public const string RegistrySubKey = @"Software\GameBot\Network";

  private static readonly System.Buffers.SearchValues<char> ForbiddenHostChars = System.Buffers.SearchValues.Create("/:");

  public static PersistedNetworkValues Read(string storageRoot, Func<string, string?>? registryReader = null) {
    var problems = new List<string>();
    string? fileHost = null;
    int? filePort = null;

    try {
      ReadFile(Path.Combine(storageRoot, "config", FileName), problems, out fileHost, out filePort);
    }
    catch (Exception ex) when (ex is not OutOfMemoryException) {
      problems.Add($"The network settings file '{FileName}' cannot be read: {ex.Message}");
    }

    string? host = fileHost;
    int? port = filePort;
    if (host is null || port is null) {
      var reader = registryReader ?? ReadRegistryValue;
      if (host is null) {
        var candidate = SafeRead(reader, "BindHost");
        if (candidate is not null && IsValidHost(candidate)) {
          host = candidate;
        }
      }

      if (port is null) {
        var candidate = SafeRead(reader, "Port");
        if (candidate is not null && TryParsePort(candidate, out var parsed)) {
          port = parsed;
        }
      }
    }

    return new PersistedNetworkValues(host, port, problems);
  }

  /// <summary>Reads a value of the older registry key (current user). Returns null when it is absent.</summary>
  public static string? ReadRegistryValue(string name) {
    if (!OperatingSystem.IsWindows()) {
      return null;
    }

    var value = Registry.GetValue($@"HKEY_CURRENT_USER\{RegistrySubKey}", name, null)?.ToString();
    return string.IsNullOrWhiteSpace(value) ? null : value;
  }

  /// <summary>A host is valid when it is not empty and has no space, '/' or ':' character.</summary>
  public static bool IsValidHost(string? host) =>
    !string.IsNullOrEmpty(host)
    && !host.AsSpan().ContainsAny(ForbiddenHostChars)
    && !host.Any(char.IsWhiteSpace);

  /// <summary>A port is valid when it is a whole number from 1 to 65535.</summary>
  public static bool TryParsePort(string? text, out int port) {
    port = 0;
    if (string.IsNullOrWhiteSpace(text)) {
      return false;
    }

    return int.TryParse(text.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out port)
           && port >= 1 && port <= 65535;
  }

  private static string? SafeRead(Func<string, string?> reader, string name) {
    try {
      var value = reader(name);
      return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
    catch (Exception ex) when (ex is not OutOfMemoryException) {
      return null;
    }
  }

  private static void ReadFile(string path, List<string> problems, out string? host, out int? port) {
    host = null;
    port = null;
    if (!File.Exists(path)) {
      return;
    }

    JsonDocument document;
    try {
      document = JsonDocument.Parse(File.ReadAllText(path));
    }
    catch (JsonException) {
      problems.Add($"The network settings file '{FileName}' is not valid JSON. The file is ignored.");
      return;
    }

    using (document) {
      var root = document.RootElement;
      if (root.ValueKind != JsonValueKind.Object) {
        problems.Add($"The network settings file '{FileName}' does not hold a JSON object. The file is ignored.");
        return;
      }

      if (root.TryGetProperty("bindHost", out var hostElement)) {
        var text = hostElement.ValueKind == JsonValueKind.String ? hostElement.GetString() : null;
        if (IsValidHost(text)) {
          host = text;
        }
        else {
          problems.Add($"The field 'bindHost' in '{FileName}' is not valid. It needs a host name or address with no space, '/' or ':'. The value is ignored.");
        }
      }

      if (root.TryGetProperty("port", out var portElement)) {
        var text = portElement.ValueKind switch {
          JsonValueKind.String => portElement.GetString(),
          JsonValueKind.Number => portElement.GetRawText(),
          _ => null
        };
        if (TryParsePort(text, out var parsed)) {
          port = parsed;
        }
        else {
          problems.Add($"The field 'port' in '{FileName}' is not valid. It needs a whole number from 1 to 65535. The value is ignored.");
        }
      }
    }
  }
}
