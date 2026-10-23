using System.Globalization;
using System.Text.Json;

namespace GameBot.Service.Services;

/// <summary>The saved listen host and port. A null field means "no valid saved value".</summary>
internal sealed record PersistedNetworkValues(string? BindHost, int? Port, IReadOnlyList<string> Problems);

/// <summary>
/// Reads the saved listen host and port from <c>&lt;data root&gt;\config\network.json</c>. Only the installer writes
/// the file. The installer moves the older registry values into the file, so the bot does not read the registry.
/// Each field is checked alone. This class never throws and never writes.
/// </summary>
internal static class PersistedNetworkSettings {
  public const string FileName = "network.json";

  private static readonly System.Buffers.SearchValues<char> ForbiddenHostChars = System.Buffers.SearchValues.Create("/:");

  public static PersistedNetworkValues Read(string storageRoot) {
    var problems = new List<string>();
    string? host = null;
    int? port = null;

    try {
      ReadFile(Path.Combine(storageRoot, "config", FileName), problems, out host, out port);
    }
    catch (Exception ex) when (ex is not OutOfMemoryException) {
      problems.Add($"The network settings file '{FileName}' cannot be read: {ex.Message}");
    }

    return new PersistedNetworkValues(host, port, problems);
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
