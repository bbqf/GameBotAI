using System.Security.Cryptography;
using Microsoft.AspNetCore.WebUtilities;

namespace GameBot.Service.Security;

/// <summary>The kind of a pending passkey ceremony.</summary>
internal enum CeremonyKind {
  SignIn,
  Registration
}

/// <summary>A pending ceremony: the challenge options that the service made.</summary>
internal sealed record PendingCeremony(
  string Id,
  CeremonyKind Kind,
  string Source,
  PasskeyOptions Options,
  string? CodeHash,
  DateTimeOffset ExpiresUtc);

/// <summary>
/// The challenges that wait for an answer (feature 187, research R8). They live 5 minutes in memory.
/// The cache keeps at most 100 items and at most 5 for each source, so one source cannot remove the sign-in of the owner.
/// </summary>
internal sealed class PendingCeremonyCache {
  internal const int MaxTotal = 100;
  internal const int MaxPerSource = 5;
  internal static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(5);

  private readonly TimeProvider _time;
  private readonly List<PendingCeremony> _items = new();
  private readonly object _gate = new();

  public PendingCeremonyCache(TimeProvider time) {
    _time = time ?? throw new ArgumentNullException(nameof(time));
  }

  /// <summary>The number of items now.</summary>
  public int Count {
    get {
      lock (_gate) {
        Purge();
        return _items.Count;
      }
    }
  }

  /// <summary>Adds a ceremony and gives its id.</summary>
  public string Add(CeremonyKind kind, string source, PasskeyOptions options, string? codeHash = null) {
    var id = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(24));
    lock (_gate) {
      Purge();
      var own = _items.Where(i => string.Equals(i.Source, source, StringComparison.Ordinal)).ToList();
      while (own.Count >= MaxPerSource) {
        _items.Remove(own[0]);
        own.RemoveAt(0);
      }

      if (_items.Count >= MaxTotal) {
        var heaviest = _items.GroupBy(i => i.Source, StringComparer.Ordinal)
          .OrderByDescending(g => g.Count())
          .First()
          .Key;
        _items.Remove(_items.First(i => string.Equals(i.Source, heaviest, StringComparison.Ordinal)));
      }

      _items.Add(new PendingCeremony(id, kind, source, options, codeHash, _time.GetUtcNow() + Lifetime));
    }

    return id;
  }

  /// <summary>Takes a ceremony out of the cache. A ceremony can be used one time. Gives null for an unknown or expired id.</summary>
  public PendingCeremony? Take(string? id, CeremonyKind kind) {
    if (string.IsNullOrEmpty(id)) {
      return null;
    }

    lock (_gate) {
      Purge();
      var item = _items.FirstOrDefault(i => string.Equals(i.Id, id, StringComparison.Ordinal));
      if (item is null) {
        return null;
      }

      _items.Remove(item);
      return item.Kind == kind ? item : null;
    }
  }

  private void Purge() {
    var now = _time.GetUtcNow();
    _items.RemoveAll(i => i.ExpiresUtc <= now);
  }
}
