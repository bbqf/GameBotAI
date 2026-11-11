using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.WebUtilities;

namespace GameBot.Service.Security;

/// <summary>One registered passkey (data-model "Passkey"). It holds no secret.</summary>
internal sealed class PasskeyRecord {
  public string Id { get; set; } = string.Empty;

  public string CredentialId { get; set; } = string.Empty;

  public string PublicKey { get; set; } = string.Empty;

  public uint SignCount { get; set; }

  public string Name { get; set; } = string.Empty;

  public DateTimeOffset CreatedUtc { get; set; }

  public DateTimeOffset? LastUsedUtc { get; set; }
}

/// <summary>The content of <c>passkeys.json</c>.</summary>
internal sealed class PasskeyFile {
  public string? UserHandle { get; set; }

  public List<PasskeyRecord> Passkeys { get; set; } = new();
}

/// <summary>The result of <see cref="PasskeyStore.Add"/>.</summary>
internal enum PasskeyAddResult {
  Added,
  Duplicate,
  LimitReached
}

/// <summary>The passkeys of the owner, in <c>data/auth/passkeys.json</c> (feature 187).</summary>
internal sealed class PasskeyStore {
  internal const int MaxPasskeys = 20;
  internal const int MaxNameLength = 40;

  private readonly AuthFileStore<PasskeyFile> _store;
  private readonly TimeProvider _time;

  public PasskeyStore(string authDirectory, TimeProvider time) {
    ArgumentNullException.ThrowIfNull(authDirectory);
    _time = time ?? throw new ArgumentNullException(nameof(time));
    _store = new AuthFileStore<PasskeyFile>(Path.Combine(authDirectory, "passkeys.json"), time);
  }

  /// <summary>The passkeys now. The list is shared. Do not change it.</summary>
  public IReadOnlyList<PasskeyRecord> List() => _store.Snapshot().Passkeys;

  /// <summary>The number of registered passkeys.</summary>
  public int Count => _store.Snapshot().Passkeys.Count;

  /// <summary>True when a passkey with this id exists.</summary>
  public bool Exists(string id) => _store.Snapshot().Passkeys.Any(p => string.Equals(p.Id, id, StringComparison.Ordinal));

  /// <summary>Finds a passkey by its credential id (base64url text).</summary>
  public PasskeyRecord? FindByCredentialId(string credentialId) =>
    _store.Snapshot().Passkeys.FirstOrDefault(p => string.Equals(p.CredentialId, credentialId, StringComparison.Ordinal));

  /// <summary>The fixed user handle (16 random bytes) or null before the first registration.</summary>
  public byte[]? UserHandle() {
    var handle = _store.Snapshot().UserHandle;
    return handle is null ? null : WebEncoders.Base64UrlDecode(handle);
  }

  /// <summary>Gives the user handle and makes it at the first registration.</summary>
  public byte[] GetOrCreateUserHandle() {
    var existing = UserHandle();
    if (existing is not null) {
      return existing;
    }

    return _store.Update(file => {
      file.UserHandle ??= WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(16));
      return WebEncoders.Base64UrlDecode(file.UserHandle);
    });
  }

  /// <summary>Adds a passkey. The result tells if the passkey was added.</summary>
  public PasskeyAddResult Add(string credentialId, string publicKey, uint signCount, string? name, out PasskeyRecord? added) {
    PasskeyRecord? record = null;
    var result = _store.Update(file => {
      if (file.Passkeys.Any(p => string.Equals(p.CredentialId, credentialId, StringComparison.Ordinal))) {
        return PasskeyAddResult.Duplicate;
      }

      if (file.Passkeys.Count >= MaxPasskeys) {
        return PasskeyAddResult.LimitReached;
      }

      var now = _time.GetUtcNow();
      record = new PasskeyRecord {
        Id = MakeShortId(credentialId, file.Passkeys),
        CredentialId = credentialId,
        PublicKey = publicKey,
        SignCount = signCount,
        Name = NormalizeName(name) ?? string.Create(CultureInfo.InvariantCulture, $"Device {now:yyyy-MM-dd}"),
        CreatedUtc = now
      };
      file.Passkeys.Add(record);
      return PasskeyAddResult.Added;
    });
    added = record;
    return result;
  }

  /// <summary>Saves the counter and the last use time after a correct sign-in.</summary>
  public void RecordSignIn(string id, uint signCount) {
    _store.Update(file => {
      var passkey = file.Passkeys.FirstOrDefault(p => string.Equals(p.Id, id, StringComparison.Ordinal));
      if (passkey is null) {
        return;
      }

      passkey.SignCount = signCount;
      passkey.LastUsedUtc = _time.GetUtcNow();
    });
  }

  /// <summary>Changes the name. Returns the changed record, or null for an unknown id.</summary>
  public PasskeyRecord? Rename(string id, string name) {
    var normal = NormalizeName(name) ?? throw new ArgumentException("The name is not valid.", nameof(name));
    return _store.Update(file => {
      var passkey = file.Passkeys.FirstOrDefault(p => string.Equals(p.Id, id, StringComparison.Ordinal));
      if (passkey is not null) {
        passkey.Name = normal;
      }

      return passkey;
    });
  }

  /// <summary>Removes a passkey. Returns false for an unknown id. The caller ends the sessions of the passkey.</summary>
  public bool Remove(string id) =>
    _store.Update(file => file.Passkeys.RemoveAll(p => string.Equals(p.Id, id, StringComparison.Ordinal)) > 0);

  /// <summary>Trims the name and checks its length. Returns null when the name is not valid.</summary>
  public static string? NormalizeName(string? name) {
    var trimmed = name?.Trim();
    return string.IsNullOrEmpty(trimmed) || trimmed.Length > MaxNameLength ? null : trimmed;
  }

  private static string MakeShortId(string credentialId, List<PasskeyRecord> existing) {
    var hex = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(credentialId))).ToLowerInvariant();
    for (var length = 8; length < hex.Length; length++) {
      var candidate = hex[..length];
      if (!existing.Any(p => string.Equals(p.Id, candidate, StringComparison.Ordinal))) {
        return candidate;
      }
    }

    return hex;
  }
}
