namespace GameBot.Domain.Gamepacks;

/// <summary>The options of one install request (feature 149).</summary>
/// <param name="DryRun">True to check only. No data changes.</param>
/// <param name="TakeOver">True to take over user objects of the same type and name.</param>
/// <param name="SignatureText">The detached signature file text, or null when none was sent.</param>
/// <param name="Force">
/// True to install the same version again (feature 178). It has an effect only when the gamepack is installed with the same
/// version as the file. The install then writes a reinstall point and lists the overwritten objects.
/// </param>
public sealed record GamepackInstallOptions(bool DryRun = false, bool TakeOver = false, string? SignatureText = null, bool Force = false);
