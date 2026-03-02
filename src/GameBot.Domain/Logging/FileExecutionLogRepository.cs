using System.Globalization;
using System.Collections.Immutable;
using System.Text.Json;

namespace GameBot.Domain.Logging;

public sealed class FileExecutionLogRepository : IExecutionLogRepository, IDisposable {
  private readonly string _dir;
  private readonly SemaphoreSlim _mutex = new(1, 1);
  private ImmutableArray<ExecutionLogEntry> _entries = ImmutableArray<ExecutionLogEntry>.Empty;
  private bool _entriesLoaded;
  private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) {
    WriteIndented = true
  };

  public FileExecutionLogRepository(string storageRoot) {
    _dir = Path.Combine(storageRoot, "execution-logs");
    Directory.CreateDirectory(_dir);
  }

  public async Task AddAsync(ExecutionLogEntry entry, CancellationToken ct = default) {
    ArgumentNullException.ThrowIfNull(entry);
    var path = Path.Combine(_dir, entry.Id + ".json");
    await _mutex.WaitAsync(ct).ConfigureAwait(false);
    try {
      using var fs = File.Create(path);
      await JsonSerializer.SerializeAsync(fs, entry, JsonOptions, ct).ConfigureAwait(false);
      if (_entriesLoaded) {
        _entries = _entries.Add(entry);
      }
    }
    finally {
      _mutex.Release();
    }
  }

  public async Task<ExecutionLogEntry?> GetAsync(string id, CancellationToken ct = default) {
    if (string.IsNullOrWhiteSpace(id)) return null;
    var path = Path.Combine(_dir, id + ".json");
    if (!File.Exists(path)) return null;
    using var fs = File.OpenRead(path);
    return await JsonSerializer.DeserializeAsync<ExecutionLogEntry>(fs, JsonOptions, ct).ConfigureAwait(false);
  }

  public async Task<ExecutionLogPage> QueryAsync(ExecutionLogQuery query, CancellationToken ct = default) {
    query ??= new ExecutionLogQuery();
    var pageSize = Math.Clamp(query.PageSize <= 0 ? 50 : query.PageSize, 1, 200);
    await EnsureEntriesLoadedAsync(ct).ConfigureAwait(false);

    var entries = new List<ExecutionLogEntry>();
    foreach (var item in _entries) {
      ct.ThrowIfCancellationRequested();
      if (query.FromUtc.HasValue && item.TimestampUtc < query.FromUtc.Value) continue;
      if (query.ToUtc.HasValue && item.TimestampUtc > query.ToUtc.Value) continue;
      if (!string.IsNullOrWhiteSpace(query.FinalStatus) && !string.Equals(item.FinalStatus, query.FinalStatus, StringComparison.OrdinalIgnoreCase)) continue;
      if (!string.IsNullOrWhiteSpace(query.ObjectType) && !string.Equals(item.ObjectRef.ObjectType, query.ObjectType, StringComparison.OrdinalIgnoreCase)) continue;
      if (!string.IsNullOrWhiteSpace(query.ObjectId) && !string.Equals(item.ObjectRef.ObjectId, query.ObjectId, StringComparison.OrdinalIgnoreCase)) continue;
      if (!string.IsNullOrWhiteSpace(query.FilterStatus) && !ContainsIgnoreCase(item.FinalStatus, query.FilterStatus)) continue;
      if (!string.IsNullOrWhiteSpace(query.FilterObjectName) && !ContainsIgnoreCase(item.ObjectRef.DisplayNameSnapshot, query.FilterObjectName)) continue;
      if (!string.IsNullOrWhiteSpace(query.FilterTimestamp) && !ContainsIgnoreCase(FormatTimestampSearchText(item.TimestampUtc), query.FilterTimestamp)) continue;
      entries.Add(item);
    }

    var sortBy = (query.SortBy ?? string.Empty).Trim().ToUpperInvariant();
    var direction = (query.SortDirection ?? string.Empty).Trim().ToUpperInvariant();
    var ascending = string.Equals(direction, "ASC", StringComparison.Ordinal);

    var ordered = sortBy switch {
      "OBJECTNAME" => ascending
        ? entries.OrderBy(e => e.ObjectRef.DisplayNameSnapshot, StringComparer.OrdinalIgnoreCase).ThenByDescending(e => e.TimestampUtc).ThenByDescending(e => e.Id)
        : entries.OrderByDescending(e => e.ObjectRef.DisplayNameSnapshot, StringComparer.OrdinalIgnoreCase).ThenByDescending(e => e.TimestampUtc).ThenByDescending(e => e.Id),
      "STATUS" => ascending
        ? entries.OrderBy(e => e.FinalStatus, StringComparer.OrdinalIgnoreCase).ThenByDescending(e => e.TimestampUtc).ThenByDescending(e => e.Id)
        : entries.OrderByDescending(e => e.FinalStatus, StringComparer.OrdinalIgnoreCase).ThenByDescending(e => e.TimestampUtc).ThenByDescending(e => e.Id),
      _ => ascending
        ? entries.OrderBy(e => e.TimestampUtc).ThenByDescending(e => e.Id)
        : entries.OrderByDescending(e => e.TimestampUtc).ThenByDescending(e => e.Id)
    };

    var startIndex = 0;
    var pageToken = string.IsNullOrWhiteSpace(query.PageToken) ? query.Cursor : query.PageToken;
    if (!string.IsNullOrWhiteSpace(pageToken)) {
      if (int.TryParse(pageToken, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) && parsed >= 0) {
        startIndex = parsed;
      }
    }

    var orderedList = ordered.ToList();

    if (startIndex >= orderedList.Count) {
      return new ExecutionLogPage(Array.Empty<ExecutionLogEntry>(), null);
    }

    var items = orderedList.Skip(startIndex).Take(pageSize).ToList();
    var nextIndex = startIndex + items.Count;
    var nextCursor = nextIndex < orderedList.Count ? nextIndex.ToString(CultureInfo.InvariantCulture) : null;

    return new ExecutionLogPage(items, nextCursor);
  }

  public async Task<int> DeleteExpiredAsync(DateTimeOffset nowUtc, CancellationToken ct = default) {
    await EnsureEntriesLoadedAsync(ct).ConfigureAwait(false);
    var deleted = 0;
    var remaining = ImmutableArray.CreateBuilder<ExecutionLogEntry>(_entries.Length);
    foreach (var entry in _entries) {
      ct.ThrowIfCancellationRequested();
      if (entry.RetentionExpiresUtc <= nowUtc) {
        try {
          var file = Path.Combine(_dir, entry.Id + ".json");
          File.Delete(file);
          deleted++;
        }
        catch {
        }
      }
      else {
        remaining.Add(entry);
      }
    }

    _entries = remaining.ToImmutable();

    return deleted;
  }

  public void Dispose() {
    _mutex.Dispose();
  }

  private static bool ContainsIgnoreCase(string source, string value)
    => source.Contains(value, StringComparison.OrdinalIgnoreCase);

  private static string FormatTimestampSearchText(DateTimeOffset timestampUtc) {
    var local = timestampUtc.ToLocalTime();
    return $"{local:O} {local:G} {local:g}";
  }

  private async Task EnsureEntriesLoadedAsync(CancellationToken ct) {
    if (_entriesLoaded) {
      return;
    }

    await _mutex.WaitAsync(ct).ConfigureAwait(false);
    try {
      if (_entriesLoaded) {
        return;
      }

      var loaded = ImmutableArray.CreateBuilder<ExecutionLogEntry>();
      foreach (var file in Directory.EnumerateFiles(_dir, "*.json")) {
        ct.ThrowIfCancellationRequested();
        using var fs = File.OpenRead(file);
        var item = await JsonSerializer.DeserializeAsync<ExecutionLogEntry>(fs, JsonOptions, ct).ConfigureAwait(false);
        if (item is not null) {
          loaded.Add(item);
        }
      }

      _entries = loaded.ToImmutable();
      _entriesLoaded = true;
    }
    finally {
      _mutex.Release();
    }
  }
}
