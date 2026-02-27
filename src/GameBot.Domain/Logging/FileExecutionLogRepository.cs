using System.Globalization;
using System.Text.Json;

namespace GameBot.Domain.Logging;

public sealed class FileExecutionLogRepository : IExecutionLogRepository, IDisposable
{
  private readonly string _dir;
  private readonly SemaphoreSlim _mutex = new(1, 1);
  private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
  {
    WriteIndented = true
  };

  public FileExecutionLogRepository(string storageRoot)
  {
    _dir = Path.Combine(storageRoot, "execution-logs");
    Directory.CreateDirectory(_dir);
  }

  public async Task AddAsync(ExecutionLogEntry entry, CancellationToken ct = default)
  {
    ArgumentNullException.ThrowIfNull(entry);
    var path = Path.Combine(_dir, entry.Id + ".json");
    await _mutex.WaitAsync(ct).ConfigureAwait(false);
    try
    {
      using var fs = File.Create(path);
      await JsonSerializer.SerializeAsync(fs, entry, JsonOptions, ct).ConfigureAwait(false);
    }
    finally
    {
      _mutex.Release();
    }
  }

  public async Task<ExecutionLogEntry?> GetAsync(string id, CancellationToken ct = default)
  {
    if (string.IsNullOrWhiteSpace(id)) return null;
    var path = Path.Combine(_dir, id + ".json");
    if (!File.Exists(path)) return null;
    using var fs = File.OpenRead(path);
    return await JsonSerializer.DeserializeAsync<ExecutionLogEntry>(fs, JsonOptions, ct).ConfigureAwait(false);
  }

  public async Task<ExecutionLogPage> QueryAsync(ExecutionLogQuery query, CancellationToken ct = default)
  {
    query ??= new ExecutionLogQuery();
    var pageSize = Math.Clamp(query.PageSize <= 0 ? 50 : query.PageSize, 1, 200);

    var entries = new List<ExecutionLogEntry>();
    foreach (var file in Directory.EnumerateFiles(_dir, "*.json"))
    {
      ct.ThrowIfCancellationRequested();
      using var fs = File.OpenRead(file);
      var item = await JsonSerializer.DeserializeAsync<ExecutionLogEntry>(fs, JsonOptions, ct).ConfigureAwait(false);
      if (item is null) continue;
      if (query.FromUtc.HasValue && item.TimestampUtc < query.FromUtc.Value) continue;
      if (query.ToUtc.HasValue && item.TimestampUtc > query.ToUtc.Value) continue;
      if (!string.IsNullOrWhiteSpace(query.FinalStatus) && !string.Equals(item.FinalStatus, query.FinalStatus, StringComparison.OrdinalIgnoreCase)) continue;
      if (!string.IsNullOrWhiteSpace(query.ObjectType) && !string.Equals(item.ObjectRef.ObjectType, query.ObjectType, StringComparison.OrdinalIgnoreCase)) continue;
      if (!string.IsNullOrWhiteSpace(query.ObjectId) && !string.Equals(item.ObjectRef.ObjectId, query.ObjectId, StringComparison.OrdinalIgnoreCase)) continue;
      entries.Add(item);
    }

    var ordered = entries.OrderByDescending(e => e.TimestampUtc).ThenByDescending(e => e.Id).ToList();

    var startIndex = 0;
    if (!string.IsNullOrWhiteSpace(query.Cursor))
    {
      if (int.TryParse(query.Cursor, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) && parsed >= 0)
      {
        startIndex = parsed;
      }
    }

    if (startIndex >= ordered.Count)
    {
      return new ExecutionLogPage(Array.Empty<ExecutionLogEntry>(), null);
    }

    var items = ordered.Skip(startIndex).Take(pageSize).ToList();
    var nextIndex = startIndex + items.Count;
    var nextCursor = nextIndex < ordered.Count ? nextIndex.ToString(CultureInfo.InvariantCulture) : null;

    return new ExecutionLogPage(items, nextCursor);
  }

  public async Task<int> DeleteExpiredAsync(DateTimeOffset nowUtc, CancellationToken ct = default)
  {
    var deleted = 0;
    foreach (var file in Directory.EnumerateFiles(_dir, "*.json"))
    {
      ct.ThrowIfCancellationRequested();
      try
      {
        var text = await File.ReadAllTextAsync(file, ct).ConfigureAwait(false);
        var entry = JsonSerializer.Deserialize<ExecutionLogEntry>(text, JsonOptions);
        if (entry is null) continue;
        if (entry.RetentionExpiresUtc <= nowUtc)
        {
          File.Delete(file);
          deleted++;
        }
      }
      catch
      {
      }
    }

    return deleted;
  }

  public void Dispose()
  {
    _mutex.Dispose();
  }
}
