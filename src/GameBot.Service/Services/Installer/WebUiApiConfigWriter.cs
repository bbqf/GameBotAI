using System.Text.Json;

namespace GameBot.Service.Services.Installer;

internal sealed class WebUiApiConfigWriter {
  private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
  private readonly string _storageRoot;

  public WebUiApiConfigWriter(string storageRoot) {
    _storageRoot = storageRoot;
  }

  public async Task WriteAsync(Uri backendBaseUrl, CancellationToken ct = default) {
    var cfgDir = Path.Combine(_storageRoot, "config");
    Directory.CreateDirectory(cfgDir);
    var cfgFile = Path.Combine(cfgDir, "web-ui-runtime.json");

    var payload = new Dictionary<string, string> {
      ["apiBaseUrl"] = backendBaseUrl.ToString()
    };

    using var stream = File.Create(cfgFile);
    await JsonSerializer.SerializeAsync(stream, payload, JsonOptions, ct).ConfigureAwait(false);
  }
}
