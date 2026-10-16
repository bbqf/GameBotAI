using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace GameBot.Domain.Gamepacks.Signing;

/// <summary>The result of a signature check, with the policy at check time.</summary>
/// <param name="Status">The status.</param>
/// <param name="Policy">The policy at check time.</param>
/// <param name="WouldReject">True only for the policy required with a status other than valid.</param>
public sealed record SignatureCheckResult(SignatureStatus Status, SignaturePolicy Policy, bool WouldReject);

/// <summary>
/// Applies the policy to the verifier (feature 146). It reads the settings on each call. Under the policy off, the verifier
/// does not run. Each check writes one log message with the status and the policy, and no key text.
/// </summary>
public sealed partial class GamepackSignatureChecker {
  private readonly IGamepackSignatureSettingsStore _settings;
  private readonly IGamepackSignatureVerifier _verifier;
  private readonly ILogger _logger;

  /// <summary>Creates the checker.</summary>
  /// <param name="settings">The settings store.</param>
  /// <param name="verifier">The verifier.</param>
  /// <param name="logger">The logger, or null.</param>
  public GamepackSignatureChecker(
      IGamepackSignatureSettingsStore settings,
      IGamepackSignatureVerifier verifier,
      ILogger<GamepackSignatureChecker>? logger = null) {
    _settings = settings ?? throw new ArgumentNullException(nameof(settings));
    _verifier = verifier ?? throw new ArgumentNullException(nameof(verifier));
    _logger = (ILogger?)logger ?? NullLogger.Instance;
  }

  /// <summary>Reads the policy that is now in force.</summary>
  public SignaturePolicy CurrentPolicy() => _settings.GetPolicy();

  /// <summary>Checks a gamepack under the policy that is now in force.</summary>
  /// <param name="gamepackJson">The gamepack file text.</param>
  /// <param name="signatureText">The signature file text, or null.</param>
  public SignatureCheckResult Check(string gamepackJson, string? signatureText) {
    ArgumentNullException.ThrowIfNull(gamepackJson);
    var settings = _settings.Load();
    var policy = settings.Policy;
    if (policy == SignaturePolicy.Off) {
      LogChecked(_logger, SignatureStatus.NotChecked.ToWire(), policy.ToWire());
      return new SignatureCheckResult(SignatureStatus.NotChecked, policy, WouldReject: false);
    }

    var status = _verifier.Verify(gamepackJson, signatureText, settings.Keys);
    LogChecked(_logger, status.ToWire(), policy.ToWire());
    return new SignatureCheckResult(status, policy, policy == SignaturePolicy.Required && status != SignatureStatus.Valid);
  }

  [LoggerMessage(EventId = 1461, Level = LogLevel.Information, Message = "Gamepack signature check: status {Status}, policy {Policy}.")]
  private static partial void LogChecked(ILogger logger, string status, string policy);
}
