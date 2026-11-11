using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json;

namespace GameBot.Service.Security;

/// <summary>The event names of the sign-in record (data-model "Sign-in record").</summary>
internal static class SignInEvents {
  public const string SignIn = "sign_in";
  public const string SignInFailed = "sign_in_failed";
  public const string SignOut = "sign_out";
  public const string SessionsEnded = "sessions_ended";
  public const string PasskeyRegistered = "passkey_registered";
  public const string RegistrationFailed = "registration_failed";
  public const string PasskeyRemoved = "passkey_removed";
  public const string SourceBlocked = "source_blocked";
  public const string RemoteAccessRefused = "remote_access_refused";
  public const string ProxyNoClientAddress = "proxy_no_client_address";
}

/// <summary>
/// Writes one JSON line for each sign-in event to <c>data/auth/sign-in-records.jsonl</c> and to the log
/// (feature 187, research R11). A line never has a token, a code, a cookie, or key material.
/// </summary>
internal sealed class SignInRecorder {
  /// <summary>The fixed text for a refusal by the sign-in function off (FR-024).</summary>
  public const string RemoteAccessRefusedDetail =
    "The sign-in function is off. Set GAMEBOT_AUTH_ENABLED=true to permit access from the internet.";

  /// <summary>The source text of events from the local command.</summary>
  public const string LocalCommandSource = "local-command";

  /// <summary>The log category of the sign-in function.</summary>
  public const string LogCategory = "GameBot.Service.Security";

  internal const long MaxFileBytes = 1024 * 1024;
  private static readonly TimeSpan RefusalInterval = TimeSpan.FromMinutes(10);
  private static readonly TimeSpan ProxyInterval = TimeSpan.FromDays(1);

  private static readonly Action<ILogger, string, string, string?, string?, Exception?> LogEvent =
    LoggerMessage.Define<string, string, string?, string?>(
      LogLevel.Information,
      new EventId(18701, "SignInEvent"),
      "Sign-in event {Event} from {Source} (passkey {PasskeyId}): {Detail}");

  private readonly string _file;
  private readonly TimeProvider _time;
  private readonly ILogger? _logger;
  private readonly object _gate = new();
  private readonly ConcurrentDictionary<string, DateTimeOffset> _lastRefusal = new(StringComparer.Ordinal);
  private DateTimeOffset _lastProxyRecord = DateTimeOffset.MinValue;

  public SignInRecorder(string authDirectory, TimeProvider time, ILogger? logger = null) {
    ArgumentNullException.ThrowIfNull(authDirectory);
    _file = Path.Combine(authDirectory, "sign-in-records.jsonl");
    _time = time ?? throw new ArgumentNullException(nameof(time));
    _logger = logger;
  }

  /// <summary>Writes one event.</summary>
  public void Record(string eventName, string source, string? passkeyId = null, string? detail = null) {
    ArgumentNullException.ThrowIfNull(eventName);
    ArgumentNullException.ThrowIfNull(source);
    var line = JsonSerializer.Serialize(new {
      timeUtc = _time.GetUtcNow().UtcDateTime.ToString("o", CultureInfo.InvariantCulture),
      @event = eventName,
      source,
      passkeyId,
      detail
    });

    WriteLine(line);
    if (_logger is not null) {
      LogEvent(_logger, eventName, source, passkeyId, detail, null);
    }
  }

  /// <summary>Writes <c>remote_access_refused</c> at most one time in 10 minutes for each source.</summary>
  public void RecordRemoteAccessRefused(string source) {
    ArgumentNullException.ThrowIfNull(source);
    var now = _time.GetUtcNow();
    if (_lastRefusal.TryGetValue(source, out var last) && now - last < RefusalInterval) {
      return;
    }

    if (_lastRefusal.Count > 1000) {
      _lastRefusal.Clear();
    }

    _lastRefusal[source] = now;
    Record(SignInEvents.RemoteAccessRefused, source, null, RemoteAccessRefusedDetail);
  }

  /// <summary>Writes <c>proxy_no_client_address</c> at most one time in each day.</summary>
  public void RecordProxyNoClientAddress(string source) {
    var now = _time.GetUtcNow();
    lock (_gate) {
      if (now - _lastProxyRecord < ProxyInterval) {
        return;
      }

      _lastProxyRecord = now;
    }

    Record(SignInEvents.ProxyNoClientAddress, source, null, "The proxy sent no usable client address.");
  }

  private void WriteLine(string line) {
    lock (_gate) {
      try {
        Directory.CreateDirectory(Path.GetDirectoryName(_file)!);
        RollIfNeeded();
        using var stream = new FileStream(_file, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
        using var writer = new StreamWriter(stream);
        writer.WriteLine(line);
      }
      catch (IOException) {
        // A record failure must not stop a request. The log entry still exists.
      }
      catch (UnauthorizedAccessException) {
        // See above.
      }
    }
  }

  private void RollIfNeeded() {
    var info = new FileInfo(_file);
    if (!info.Exists || info.Length < MaxFileBytes) {
      return;
    }

    var old = Path.Combine(Path.GetDirectoryName(_file)!, "sign-in-records.1.jsonl");
    File.Move(_file, old, overwrite: true);
  }
}
