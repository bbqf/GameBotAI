using System.Collections.ObjectModel;
using System.Text.Json.Serialization;

namespace GameBot.Domain.Gamepacks.Signing;

/// <summary>The content of the file <c>gamepack-signature.json</c>: the policy and the trusted keys.</summary>
public sealed class GamepackSignatureSettings {
  /// <summary>The policy. The default is off.</summary>
  public SignaturePolicy Policy { get; set; } = SignaturePolicy.Off;

  /// <summary>The trusted keys. Fingerprints are unique.</summary>
  [JsonObjectCreationHandling(JsonObjectCreationHandling.Populate)]
  public Collection<TrustedKey> Keys { get; } = new();
}
