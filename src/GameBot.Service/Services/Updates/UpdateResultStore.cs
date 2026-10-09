using System.Text.Json;
using System.Text.Json.Serialization;
using GameBot.Domain.Updates;

namespace GameBot.Service.Services.Updates;

/// <summary>
/// Reads and writes <c>update-result.json</c> in the updates folder. The bot writes the state
/// "installing" before it exits. The updater program writes the final state. The bot reads the
/// file at the next start and renames it, so the UI shows the result one time (research R-008).
/// </summary>
internal sealed class UpdateResultStore : IUpdateResultStore {
  private static readonly TimeSpan LogRetention = TimeSpan.FromDays(30);

  private static readonly JsonSerializerOptions Options = CreateOptions();

  private readonly UpdatePaths _paths;
  private readonly ILogger<UpdateResultStore> _logger;

  public UpdateResultStore(UpdatePaths paths, ILogger<UpdateResultStore> logger) {
    _paths = paths;
    _logger = logger;
  }

  public async Task<UpdateAttempt?> ReadAsync(CancellationToken ct) {
    if (!File.Exists(_paths.ResultFile)) {
      return null;
    }

    try {
      var stream = new FileStream(_paths.ResultFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
      await using (stream.ConfigureAwait(false)) {
        return await JsonSerializer.DeserializeAsync<UpdateAttempt>(stream, Options, ct).ConfigureAwait(false);
      }
    }
    catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException) {
      UpdateLog.ResultUnreadable(_logger, ex.Message);
      return null;
    }
  }

  public async Task WriteAsync(UpdateAttempt attempt, CancellationToken ct) {
    Directory.CreateDirectory(_paths.Directory);
    // Write to a temporary file first, so a reader never sees half a file.
    var temp = _paths.ResultFile + ".tmp";
    var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None);
    await using (stream.ConfigureAwait(false)) {
      await JsonSerializer.SerializeAsync(stream, attempt, Options, ct).ConfigureAwait(false);
    }

    File.Move(temp, _paths.ResultFile, overwrite: true);
  }

  public void MarkReported() {
    if (!File.Exists(_paths.ResultFile)) {
      return;
    }

    try {
      File.Move(_paths.ResultFile, _paths.ReportedResultFile, overwrite: true);
    }
    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) {
      UpdateLog.ResultUnreadable(_logger, ex.Message);
    }
  }

  public void Cleanup(DateTimeOffset now) {
    if (!Directory.Exists(_paths.Directory)) {
      return;
    }

    try {
      foreach (var msi in Directory.EnumerateFiles(_paths.Directory, "GameBot-*.msi")) {
        File.Delete(msi);
      }

      if (Directory.Exists(_paths.UpdaterCopyDirectory)) {
        Directory.Delete(_paths.UpdaterCopyDirectory, recursive: true);
      }

      foreach (var log in Directory.EnumerateFiles(_paths.Directory, "msiexec-*.log")) {
        if (now - File.GetLastWriteTimeUtc(log) > LogRetention) {
          File.Delete(log);
        }
      }
    }
    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) {
      // The updater copy can still run in the first seconds. The next cleanup removes it.
      UpdateLog.CleanupFailed(_logger, ex.Message);
    }
  }

  private static JsonSerializerOptions CreateOptions() {
    var options = new JsonSerializerOptions(JsonSerializerDefaults.Web) {
      WriteIndented = true,
      DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };
    options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
    return options;
  }
}
