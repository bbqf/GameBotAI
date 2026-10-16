namespace GameBot.Domain.Gamepacks;

/// <summary>The options of one install request (feature 149).</summary>
/// <param name="DryRun">True to check only. No data changes.</param>
/// <param name="TakeOver">True to take over user objects of the same type and name.</param>
/// <param name="SignatureText">The detached signature file text, or null when none was sent.</param>
public sealed record GamepackInstallOptions(bool DryRun = false, bool TakeOver = false, string? SignatureText = null);
