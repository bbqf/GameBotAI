namespace GameBot.Domain.Gamepacks.Signing;

/// <summary>The result of an add-key request.</summary>
public enum AddKeyStatus {
  /// <summary>The key was added.</summary>
  Added,

  /// <summary>The label is empty or longer than 100 characters.</summary>
  InvalidLabel,

  /// <summary>The key is not base64 SPKI of an ECDSA P-256 key.</summary>
  InvalidKey,

  /// <summary>A key with the same fingerprint is on the list.</summary>
  Duplicate
}

/// <summary>The outcome of an add-key request.</summary>
/// <param name="Status">The status.</param>
/// <param name="Key">The new key, when the status is <see cref="AddKeyStatus.Added"/>.</param>
public sealed record AddKeyOutcome(AddKeyStatus Status, TrustedKey? Key);

/// <summary>Keeps the policy and the trusted keys of the gamepack signature check (feature 146).</summary>
public interface IGamepackSignatureSettingsStore {
  /// <summary>Reads the settings from disk. A read happens on each call, so a change needs no restart.</summary>
  GamepackSignatureSettings Load();

  /// <summary>Reads the policy.</summary>
  SignaturePolicy GetPolicy();

  /// <summary>Saves the policy.</summary>
  /// <param name="policy">The new policy.</param>
  void SetPolicy(SignaturePolicy policy);

  /// <summary>Adds a trusted key. Nothing is saved when the request is not valid.</summary>
  /// <param name="label">The label.</param>
  /// <param name="publicKey">The base64 SPKI text.</param>
  AddKeyOutcome AddKey(string? label, string? publicKey);

  /// <summary>Removes a trusted key. Gives false when no key has this fingerprint.</summary>
  /// <param name="fingerprint">The fingerprint.</param>
  bool RemoveKey(string fingerprint);
}
