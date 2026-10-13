using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace GameBot.Domain.Queues;

/// <summary>
/// File-backed <see cref="ISentAlertStore"/> (feature 137). It keeps one JSON file at
/// <c>&lt;dataRoot&gt;/notify-alert-dedupe/sent-alerts.json</c>. One gate serializes all access. A write
/// goes to a temporary file first and then replaces the file. Claims live in memory only and die after
/// 5 minutes. A commit removes records that are older than 48 hours.
/// </summary>
public sealed partial class FileSentAlertStore : ISentAlertStore, IDisposable {
  /// <summary>The name of the folder under the data root.</summary>
  public const string FolderName = "notify-alert-dedupe";

  /// <summary>The name of the file in the folder.</summary>
  public const string FileName = "sent-alerts.json";

  /// <summary>The schema version that this store writes and reads.</summary>
  public const int CurrentSchemaVersion = 1;

  /// <summary>How long a claim lives.</summary>
  public static readonly TimeSpan ReservationLifetime = TimeSpan.FromMinutes(5);

  /// <summary>How long a record lives.</summary>
  public static readonly TimeSpan RecordRetention = TimeSpan.FromHours(48);

  private static readonly JsonSerializerOptions JsonOptions = new() {
    WriteIndented = true,
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
  };

  private readonly string _folder;
  private readonly string _path;
  private readonly ILogger _logger;
  private readonly SemaphoreSlim _gate = new(1, 1);
  private readonly Dictionary<(string Queue, string Key), DateTimeOffset> _records = new();
  private readonly Dictionary<(string Queue, string Key), SentAlertReservation> _reservations = new();

  /// <summary>Creates the store on <paramref name="dataRoot"/> and loads the file.</summary>
  public FileSentAlertStore(string dataRoot, ILogger<FileSentAlertStore>? logger = null) {
    ArgumentException.ThrowIfNullOrWhiteSpace(dataRoot);
    _folder = Path.Combine(dataRoot, FolderName);
    Directory.CreateDirectory(_folder);
    _path = Path.Combine(_folder, FileName);
    _logger = (ILogger?)logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance;
    Load();
  }

  /// <summary>The folder that holds the file.</summary>
  public string FolderPath => _folder;

  /// <inheritdoc />
  public async Task<SentAlertReservation?> TryReserveAsync(string queueId, string key, DateTimeOffset windowStart, DateTimeOffset now, CancellationToken ct = default) {
    ArgumentException.ThrowIfNullOrWhiteSpace(queueId);
    ArgumentException.ThrowIfNullOrWhiteSpace(key);
    await _gate.WaitAsync(ct).ConfigureAwait(false);
    try {
      var scope = (queueId, key);
      if (_records.TryGetValue(scope, out var sentAt) && sentAt >= windowStart) return null;
      if (_reservations.TryGetValue(scope, out var live) && IsLive(live, now)) return null;

      var reservation = new SentAlertReservation(queueId, key, now);
      _reservations[scope] = reservation;
      return reservation;
    }
    finally {
      _gate.Release();
    }
  }

  /// <inheritdoc />
  public async Task CommitAsync(SentAlertReservation reservation, DateTimeOffset sentAt, CancellationToken ct = default) {
    ArgumentNullException.ThrowIfNull(reservation);
    await _gate.WaitAsync(ct).ConfigureAwait(false);
    try {
      var scope = (reservation.QueueId, reservation.Key);
      DropIfSame(scope, reservation);
      _records[scope] = sentAt;
      var oldest = sentAt - RecordRetention;
      foreach (var old in _records.Where(pair => pair.Value < oldest).Select(pair => pair.Key).ToList()) {
        _records.Remove(old);
      }

      Write();
    }
    finally {
      _gate.Release();
    }
  }

  /// <inheritdoc />
  public async Task ReleaseAsync(SentAlertReservation reservation, CancellationToken ct = default) {
    ArgumentNullException.ThrowIfNull(reservation);
    await _gate.WaitAsync(ct).ConfigureAwait(false);
    try {
      DropIfSame((reservation.QueueId, reservation.Key), reservation);
    }
    finally {
      _gate.Release();
    }
  }

  /// <inheritdoc />
  public async Task<DateTimeOffset?> WasSentSinceAsync(string queueId, string key, DateTimeOffset windowStart, CancellationToken ct = default) {
    await _gate.WaitAsync(ct).ConfigureAwait(false);
    try {
      return _records.TryGetValue((queueId, key), out var sentAt) && sentAt >= windowStart ? sentAt : null;
    }
    finally {
      _gate.Release();
    }
  }

  /// <inheritdoc />
  public async Task<bool> HasLiveReservationAsync(string queueId, string key, DateTimeOffset now, CancellationToken ct = default) {
    await _gate.WaitAsync(ct).ConfigureAwait(false);
    try {
      return _reservations.TryGetValue((queueId, key), out var live) && IsLive(live, now);
    }
    finally {
      _gate.Release();
    }
  }

  /// <inheritdoc />
  public async Task DeleteQueueAsync(string queueId, CancellationToken ct = default) {
    ArgumentException.ThrowIfNullOrWhiteSpace(queueId);
    await _gate.WaitAsync(ct).ConfigureAwait(false);
    try {
      foreach (var scope in _reservations.Keys.Where(k => k.Queue == queueId).ToList()) _reservations.Remove(scope);
      var removed = false;
      foreach (var scope in _records.Keys.Where(k => k.Queue == queueId).ToList()) {
        _records.Remove(scope);
        removed = true;
      }

      if (removed) Write();
    }
    finally {
      _gate.Release();
    }
  }

  /// <inheritdoc />
  public void Dispose() => _gate.Dispose();

  private static bool IsLive(SentAlertReservation reservation, DateTimeOffset now) =>
    now - reservation.ReservedAt < ReservationLifetime;

  // Call only while the gate is held. A stale token must not drop the claim of a newer run.
  private void DropIfSame((string Queue, string Key) scope, SentAlertReservation reservation) {
    if (_reservations.TryGetValue(scope, out var current) && ReferenceEquals(current, reservation)) {
      _reservations.Remove(scope);
    }
  }

  private void Load() {
    if (!File.Exists(_path)) return;
    try {
      var doc = JsonSerializer.Deserialize<AlertsDocument>(File.ReadAllText(_path), JsonOptions);
      if (doc is null || doc.SchemaVersion is < 1 or > CurrentSchemaVersion) {
        Log.DamagedFile(_logger, _path, null);
        return;
      }

      foreach (var record in doc.Records ?? new List<AlertRecord>()) {
        if (string.IsNullOrWhiteSpace(record.QueueId) || string.IsNullOrWhiteSpace(record.Key)) continue;
        _records[(record.QueueId, record.Key)] = record.SentAt;
      }
    }
    catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException or NotSupportedException) {
      Log.DamagedFile(_logger, _path, ex);
      _records.Clear();
    }
  }

  // Call only while the gate is held.
  private void Write() {
    var doc = new AlertsDocument {
      SchemaVersion = CurrentSchemaVersion,
      Records = _records
        .OrderBy(pair => pair.Key.Queue, StringComparer.Ordinal)
        .ThenBy(pair => pair.Key.Key, StringComparer.Ordinal)
        .Select(pair => new AlertRecord { QueueId = pair.Key.Queue, Key = pair.Key.Key, SentAt = pair.Value })
        .ToList()
    };
    var temp = _path + ".tmp";
    File.WriteAllText(temp, JsonSerializer.Serialize(doc, JsonOptions));
    File.Move(temp, _path, overwrite: true);
  }

  private sealed class AlertsDocument {
    public int SchemaVersion { get; set; }
    public List<AlertRecord>? Records { get; set; }
  }

  private sealed class AlertRecord {
    public string QueueId { get; set; } = string.Empty;
    public string Key { get; set; } = string.Empty;
    public DateTimeOffset SentAt { get; set; }
  }

  private static partial class Log {
    [LoggerMessage(EventId = 13701, Level = LogLevel.Warning, Message = "The sent-alert file at {Path} is damaged or has an unknown schema version. The store starts empty.")]
    public static partial void DamagedFile(ILogger logger, string path, Exception? exception);
  }
}
