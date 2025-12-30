using System.Collections.Concurrent;

namespace GameBot.Service.Services;

internal interface ISessionContextCache {
  void SetSessionId(string gameId, string adbSerial, string sessionId);
  string? GetSessionId(string gameId, string adbSerial);
  void ClearSession(string gameId, string adbSerial);
}

internal sealed class SessionContextCache : ISessionContextCache {
  private readonly ConcurrentDictionary<string, string> _cache = new(StringComparer.OrdinalIgnoreCase);

  private static string Key(string gameId, string adbSerial) => $"{gameId}|{adbSerial}";

  public void SetSessionId(string gameId, string adbSerial, string sessionId) {
    if (string.IsNullOrWhiteSpace(gameId) || string.IsNullOrWhiteSpace(adbSerial) || string.IsNullOrWhiteSpace(sessionId)) return;
    _cache[Key(gameId.Trim(), adbSerial.Trim())] = sessionId.Trim();
  }

  public string? GetSessionId(string gameId, string adbSerial) {
    if (string.IsNullOrWhiteSpace(gameId) || string.IsNullOrWhiteSpace(adbSerial)) return null;
    _cache.TryGetValue(Key(gameId.Trim(), adbSerial.Trim()), out var value);
    return string.IsNullOrWhiteSpace(value) ? null : value;
  }

  public void ClearSession(string gameId, string adbSerial) {
    if (string.IsNullOrWhiteSpace(gameId) || string.IsNullOrWhiteSpace(adbSerial)) return;
    _cache.TryRemove(Key(gameId.Trim(), adbSerial.Trim()), out _);
  }
}
