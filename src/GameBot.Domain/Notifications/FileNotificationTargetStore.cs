using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace GameBot.Domain.Notifications {
  /// <summary>
  /// File-backed target store (feature 120). The file is <c>notifications/targets.json</c> under the
  /// data root. The store keeps the list in memory. On each read it compares the write time and the
  /// length of the file with the saved values, and it reloads the file when they differ. So a hand
  /// edit applies with no restart (FR-018). A corrupt or empty file keeps the last good list. A write
  /// goes to a temp file first and then replaces the target file.
  /// </summary>
  public sealed partial class FileNotificationTargetStore : INotificationTargetStore {
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) {
      WriteIndented = true
    };

    private readonly object _gate = new();
    private readonly string _path;
    private readonly ILogger _logger;
    private List<NotificationTarget> _targets = new();
    private (DateTime WriteTimeUtc, long Length)? _stamp;
    private bool _loaded;

    public FileNotificationTargetStore(string dataRoot, ILogger<FileNotificationTargetStore>? logger = null) {
      ArgumentException.ThrowIfNullOrWhiteSpace(dataRoot);
      var dir = Path.Combine(dataRoot, "notifications");
      Directory.CreateDirectory(dir);
      _path = Path.Combine(dir, "targets.json");
      _logger = (ILogger?)logger ?? NullLogger.Instance;
    }

    public IReadOnlyList<NotificationTarget> List() {
      lock (_gate) {
        Refresh();
        return _targets.Select(t => t.Clone()).ToList();
      }
    }

    public NotificationTarget? Find(string id) {
      lock (_gate) {
        Refresh();
        return _targets.FirstOrDefault(t => string.Equals(t.Id, id, StringComparison.Ordinal))?.Clone();
      }
    }

    public NotificationTarget Create(NotificationTarget target) {
      ArgumentNullException.ThrowIfNull(target);
      lock (_gate) {
        Refresh();
        var copy = target.Clone();
        copy.Id = Guid.NewGuid().ToString("N");
        var now = DateTimeOffset.Now;
        copy.CreatedAt = now;
        copy.UpdatedAt = now;
        var next = new List<NotificationTarget>(_targets) { copy };
        Save(next);
        return copy.Clone();
      }
    }

    public NotificationTarget? Update(NotificationTarget target) {
      ArgumentNullException.ThrowIfNull(target);
      lock (_gate) {
        Refresh();
        var index = _targets.FindIndex(t => string.Equals(t.Id, target.Id, StringComparison.Ordinal));
        if (index < 0) return null;
        var copy = target.Clone();
        copy.CreatedAt = _targets[index].CreatedAt;
        copy.UpdatedAt = DateTimeOffset.Now;
        var next = new List<NotificationTarget>(_targets) { [index] = copy };
        Save(next);
        return copy.Clone();
      }
    }

    public bool Delete(string id) {
      lock (_gate) {
        Refresh();
        var index = _targets.FindIndex(t => string.Equals(t.Id, id, StringComparison.Ordinal));
        if (index < 0) return false;
        var next = new List<NotificationTarget>(_targets);
        next.RemoveAt(index);
        Save(next);
        return true;
      }
    }

    // Reloads the file when its write time or length changed. Never throws.
    private void Refresh() {
      try {
        var info = new FileInfo(_path);
        if (!info.Exists) {
          // A file that is absent is an empty list. It is not an error.
          if (_stamp is not null || !_loaded) {
            _targets = new List<NotificationTarget>();
            _stamp = null;
          }

          _loaded = true;
          return;
        }

        var stamp = (info.LastWriteTimeUtc, info.Length);
        if (_loaded && _stamp is { } saved && saved == stamp) return;

        var loaded = TryRead();
        _loaded = true;
        _stamp = stamp;
        if (loaded is not null) _targets = loaded;
      }
      catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) {
        Log.ReadFailed(_logger, ex.GetType().Name);
      }
    }

    private List<NotificationTarget>? TryRead() {
      try {
        var text = File.ReadAllText(_path);
        if (string.IsNullOrWhiteSpace(text)) {
          Log.EmptyFile(_logger);
          return null;
        }

        var list = JsonSerializer.Deserialize<List<NotificationTarget>>(text, JsonOptions);
        if (list is null) {
          Log.EmptyFile(_logger);
          return null;
        }

        return list.Where(t => t is not null).ToList();
      }
      catch (JsonException) {
        Log.CorruptFile(_logger);
        return null;
      }
    }

    private void Save(List<NotificationTarget> next) {
      var json = JsonSerializer.Serialize(next, JsonOptions);
      var temp = _path + ".tmp";
      File.WriteAllText(temp, json);
      File.Move(temp, _path, overwrite: true);
      _targets = next;
      var info = new FileInfo(_path);
      _stamp = (info.LastWriteTimeUtc, info.Length);
      _loaded = true;
    }

    private static partial class Log {
      [LoggerMessage(EventId = 12001, Level = LogLevel.Warning, Message = "The notification target file is not valid JSON. The last good list stays.")]
      public static partial void CorruptFile(ILogger logger);

      [LoggerMessage(EventId = 12002, Level = LogLevel.Warning, Message = "The notification target file is empty. The last good list stays.")]
      public static partial void EmptyFile(ILogger logger);

      [LoggerMessage(EventId = 12003, Level = LogLevel.Warning, Message = "The notification target file could not be read. The last good list stays. Error type: {ErrorType}.")]
      public static partial void ReadFailed(ILogger logger, string errorType);
    }
  }
}
