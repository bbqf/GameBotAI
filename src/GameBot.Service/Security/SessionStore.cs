using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.WebUtilities;

namespace GameBot.Service.Security;

/// <summary>One session in <c>sessions.json</c>. The file keeps only the hash of the token.</summary>
internal sealed class SessionRecord {
  public string TokenHash { get; set; } = string.Empty;

  public string PasskeyId { get; set; } = string.Empty;

  public DateTimeOffset CreatedUtc { get; set; }

  public DateTimeOffset LastUsedUtc { get; set; }

  public DateTimeOffset ExpiresUtc { get; set; }
}

/// <summary>The content of <c>sessions.json</c>.</summary>
internal sealed class SessionFile {
  public List<SessionRecord> Sessions { get; set; } = new();
}

/// <summary>A valid session. <see cref="RefreshCookie"/> is true when the response must send a new cookie lifetime.</summary>
internal sealed record SessionInfo(string PasskeyId, string TokenHash, bool RefreshCookie);

/// <summary>Finds the valid session of a request (feature 187).</summary>
internal interface ISessionValidator {
  /// <summary>Gives the valid session of the request, or null.</summary>
  SessionInfo? Validate(HttpContext context);
}

/// <summary>The default validator. It never finds a session.</summary>
internal sealed class NoSessionValidator : ISessionValidator {
  public SessionInfo? Validate(HttpContext context) => null;
}

/// <summary>
/// The sessions of the owner (feature 187, research R4). A session lives 30 days after its last use.
/// A use moves the expiry in memory at once. The file changes at most one time in each hour for each session.
/// </summary>
internal sealed class SessionStore : ISessionValidator {
  /// <summary>The name of the session cookie.</summary>
  public const string CookieName = "__Host-gamebot-session";

  internal static readonly TimeSpan Lifetime = TimeSpan.FromDays(30);
  internal static readonly TimeSpan WriteInterval = TimeSpan.FromHours(1);

  private readonly AuthFileStore<SessionFile> _store;
  private readonly PasskeyStore _passkeys;
  private readonly TimeProvider _time;
  private readonly ConcurrentDictionary<string, DateTimeOffset> _lastUse = new(StringComparer.Ordinal);
  private readonly object _gate = new();
  private long _indexVersion = -1;
  private Dictionary<string, SessionRecord> _index = new(StringComparer.Ordinal);

  public SessionStore(string authDirectory, PasskeyStore passkeys, TimeProvider time) {
    ArgumentNullException.ThrowIfNull(authDirectory);
    _passkeys = passkeys ?? throw new ArgumentNullException(nameof(passkeys));
    _time = time ?? throw new ArgumentNullException(nameof(time));
    _store = new AuthFileStore<SessionFile>(Path.Combine(authDirectory, "sessions.json"), time);
  }

  /// <summary>The number of sessions in the file that have not expired.</summary>
  public int ActiveCount {
    get {
      var now = _time.GetUtcNow();
      return _store.Snapshot().Sessions.Count(s => s.ExpiresUtc > now);
    }
  }

  /// <summary>Makes a session for a passkey and gives the token for the cookie.</summary>
  public string Create(string passkeyId) {
    var token = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
    var now = _time.GetUtcNow();
    var record = new SessionRecord {
      TokenHash = Hash(token),
      PasskeyId = passkeyId,
      CreatedUtc = now,
      LastUsedUtc = now,
      ExpiresUtc = now + Lifetime
    };
    _store.Update(file => {
      file.Sessions.RemoveAll(s => s.ExpiresUtc <= now);
      file.Sessions.Add(record);
    });
    return token;
  }

  /// <summary>Finds the valid session for a token. A token that is not known, expired, or of a removed passkey gives null.</summary>
  public SessionInfo? Validate(string? token) {
    if (string.IsNullOrEmpty(token)) {
      return null;
    }

    var hash = Hash(token);
    var record = Lookup(hash);
    if (record is null) {
      return null;
    }

    var now = _time.GetUtcNow();
    var expires = record.ExpiresUtc;
    if (_lastUse.TryGetValue(hash, out var used) && used + Lifetime > expires) {
      expires = used + Lifetime;
    }

    if (expires <= now || !_passkeys.Exists(record.PasskeyId)) {
      return null;
    }

    _lastUse[hash] = now;
    var refresh = now - record.LastUsedUtc >= WriteInterval;
    if (refresh) {
      PersistUse(hash, now);
    }

    return new SessionInfo(record.PasskeyId, hash, refresh);
  }

  /// <inheritdoc />
  public SessionInfo? Validate(HttpContext context) {
    ArgumentNullException.ThrowIfNull(context);
    return context.Request.Cookies.TryGetValue(CookieName, out var token) ? Validate(token) : null;
  }

  /// <summary>Ends one session by the hash of its token.</summary>
  public void End(string tokenHash) {
    _store.Update(file => file.Sessions.RemoveAll(s => string.Equals(s.TokenHash, tokenHash, StringComparison.Ordinal)));
    _lastUse.TryRemove(tokenHash, out _);
  }

  /// <summary>Ends all sessions of one passkey.</summary>
  public void EndForPasskey(string passkeyId) {
    _store.Update(file => file.Sessions.RemoveAll(s => string.Equals(s.PasskeyId, passkeyId, StringComparison.Ordinal)));
  }

  /// <summary>Ends all sessions.</summary>
  public void EndAll() {
    _store.Update(file => file.Sessions.Clear());
    _lastUse.Clear();
  }

  /// <summary>Writes the session cookie. The cookie is always Secure, also when the service listener is plain HTTP.</summary>
  public static void WriteCookie(HttpResponse response, string token) {
    ArgumentNullException.ThrowIfNull(response);
    response.Cookies.Append(CookieName, token, new CookieOptions {
      Path = "/",
      Secure = true,
      HttpOnly = true,
      SameSite = SameSiteMode.Strict,
      MaxAge = Lifetime,
      IsEssential = true
    });
  }

  /// <summary>Clears the session cookie.</summary>
  public static void ClearCookie(HttpResponse response) {
    ArgumentNullException.ThrowIfNull(response);
    response.Cookies.Delete(CookieName, new CookieOptions {
      Path = "/",
      Secure = true,
      HttpOnly = true,
      SameSite = SameSiteMode.Strict
    });
  }

  internal static string Hash(string token) =>
    Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token))).ToLowerInvariant();

  private SessionRecord? Lookup(string hash) {
    lock (_gate) {
      var version = _store.Version;
      if (version != _indexVersion) {
        _index = _store.Snapshot().Sessions
          .GroupBy(s => s.TokenHash, StringComparer.Ordinal)
          .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        _indexVersion = version;
      }

      return _index.GetValueOrDefault(hash);
    }
  }

  private void PersistUse(string hash, DateTimeOffset now) {
    _store.Update(file => {
      file.Sessions.RemoveAll(s => s.ExpiresUtc <= now);
      var session = file.Sessions.FirstOrDefault(s => string.Equals(s.TokenHash, hash, StringComparison.Ordinal));
      if (session is not null) {
        session.LastUsedUtc = now;
        session.ExpiresUtc = now + Lifetime;
      }
    });
  }
}
