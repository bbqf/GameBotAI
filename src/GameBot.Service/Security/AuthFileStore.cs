using System.Text.Json;

namespace GameBot.Service.Security;

/// <summary>
/// A JSON file in <c>data/auth/</c> that the service and the local command share (feature 187, plan Design 4).
/// A change is a read-modify-write under an exclusive file lock. The store keeps a copy in memory
/// and reads the file again when its last-write time changes. It looks at the time at most one time in each second.
/// The value that <see cref="Snapshot"/> returns is shared. A caller MUST NOT change it.
/// </summary>
internal sealed class AuthFileStore<T> where T : class, new() {
  private static readonly JsonSerializerOptions JsonOptions = new() {
    WriteIndented = true,
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    PropertyNameCaseInsensitive = true
  };

  private static readonly TimeSpan CheckInterval = TimeSpan.FromSeconds(1);
  private const int LockAttempts = 60;
  private const int LockDelayMs = 25;

  private readonly string _path;
  private readonly TimeProvider _time;
  private readonly Func<T, bool>? _isEmpty;
  private readonly object _gate = new();
  private T _value = new();
  private DateTime _fileTime = DateTime.MinValue;
  private DateTimeOffset _lastCheck = DateTimeOffset.MinValue;
  private bool _loaded;
  private long _version;

  /// <param name="path">The full path of the JSON file.</param>
  /// <param name="time">The clock.</param>
  /// <param name="isEmpty">When this returns true after a change, the store deletes the file.</param>
  public AuthFileStore(string path, TimeProvider time, Func<T, bool>? isEmpty = null) {
    _path = path ?? throw new ArgumentNullException(nameof(path));
    _time = time ?? throw new ArgumentNullException(nameof(time));
    _isEmpty = isEmpty;
  }

  /// <summary>A number that grows each time the value changes in this store or in the file.</summary>
  public long Version {
    get {
      lock (_gate) {
        ReloadIfChanged();
        return _version;
      }
    }
  }

  /// <summary>The current value. It is shared. Do not change it.</summary>
  public T Snapshot() {
    lock (_gate) {
      ReloadIfChanged();
      return _value;
    }
  }

  /// <summary>Changes the value under the file lock and saves it.</summary>
  public TResult Update<TResult>(Func<T, TResult> mutate) {
    ArgumentNullException.ThrowIfNull(mutate);
    lock (_gate) {
      Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
      using var fileLock = AcquireLock();
      var current = LoadFromDisk(out var time);
      var result = mutate(current);
      Save(current);
      _value = current;
      _fileTime = File.Exists(_path) ? File.GetLastWriteTimeUtc(_path) : time;
      _loaded = true;
      _lastCheck = _time.GetUtcNow();
      _version++;
      return result;
    }
  }

  /// <summary>Changes the value under the file lock and saves it.</summary>
  public void Update(Action<T> mutate) {
    ArgumentNullException.ThrowIfNull(mutate);
    Update<bool>(value => {
      mutate(value);
      return true;
    });
  }

  private void ReloadIfChanged() {
    var now = _time.GetUtcNow();
    if (_loaded && now - _lastCheck < CheckInterval) {
      return;
    }

    _lastCheck = now;
    var time = File.Exists(_path) ? File.GetLastWriteTimeUtc(_path) : DateTime.MinValue;
    if (_loaded && time == _fileTime) {
      return;
    }

    try {
      _value = LoadFromDisk(out var loadedTime);
      _fileTime = loadedTime;
    }
    catch (IOException) {
      // The other process writes the file now. Keep the old copy and try again later.
      _lastCheck = DateTimeOffset.MinValue;
      return;
    }

    _loaded = true;
    _version++;
  }

  private T LoadFromDisk(out DateTime time) {
    if (!File.Exists(_path)) {
      time = DateTime.MinValue;
      return new T();
    }

    time = File.GetLastWriteTimeUtc(_path);
    var json = File.ReadAllText(_path);
    if (string.IsNullOrWhiteSpace(json)) {
      return new T();
    }

    try {
      return JsonSerializer.Deserialize<T>(json, JsonOptions) ?? new T();
    }
    catch (JsonException) {
      // A damaged file gives an empty value. Fail closed: no passkey and no session are valid.
      return new T();
    }
  }

  private void Save(T value) {
    if (_isEmpty is not null && _isEmpty(value)) {
      if (File.Exists(_path)) {
        File.Delete(_path);
      }

      return;
    }

    var temp = _path + ".tmp";
    File.WriteAllText(temp, JsonSerializer.Serialize(value, JsonOptions));
    if (File.Exists(_path)) {
      File.Replace(temp, _path, null);
    }
    else {
      File.Move(temp, _path);
    }
  }

  private FileStream AcquireLock() {
    var lockPath = _path + ".lock";
    for (var attempt = 0; ; attempt++) {
      try {
        return new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None, 1, FileOptions.DeleteOnClose);
      }
      catch (IOException) when (attempt < LockAttempts) {
        Thread.Sleep(LockDelayMs);
      }
      catch (UnauthorizedAccessException) when (attempt < LockAttempts) {
        Thread.Sleep(LockDelayMs);
      }
    }
  }
}
