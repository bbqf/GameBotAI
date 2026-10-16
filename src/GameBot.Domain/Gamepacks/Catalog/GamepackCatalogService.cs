using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace GameBot.Domain.Gamepacks.Catalog;

/// <summary>One version of a gamepack in the catalog list.</summary>
/// <param name="Version">The version text.</param>
/// <param name="Changelog">The change text.</param>
/// <param name="Installed">True for the installed version.</param>
/// <param name="UpdateAvailable">True when no version is installed, or this version is newer than the installed one.</param>
public sealed record CatalogVersionItem(string Version, string Changelog, bool Installed, bool UpdateAvailable);

/// <summary>One gamepack in the catalog list.</summary>
/// <param name="Name">The gamepack name.</param>
/// <param name="InstalledVersion">The installed version, or null.</param>
/// <param name="Versions">The versions from new to old.</param>
public sealed record CatalogListItem(string Name, string? InstalledVersion, IReadOnlyList<CatalogVersionItem> Versions);

/// <summary>
/// The catalog service (feature 147): list and install. Nothing here runs without a request. The service has no cache, no timer,
/// and no background work (FR-015). A failure throws <see cref="CatalogException"/> and leaves the instance as it was.
/// </summary>
public sealed partial class GamepackCatalogService {
  private readonly GamepackCatalogSourceFactory _factory;
  private readonly GamepackService _gamepacks;
  private readonly CatalogOptions _options;
  private readonly ILogger _logger;

  /// <summary>Creates the service.</summary>
  /// <param name="factory">The source factory.</param>
  /// <param name="gamepacks">The gamepack service.</param>
  /// <param name="options">The limits.</param>
  /// <param name="logger">The logger, or null.</param>
  public GamepackCatalogService(
      GamepackCatalogSourceFactory factory,
      GamepackService gamepacks,
      CatalogOptions options,
      ILogger<GamepackCatalogService>? logger = null) {
    _factory = factory ?? throw new ArgumentNullException(nameof(factory));
    _gamepacks = gamepacks ?? throw new ArgumentNullException(nameof(gamepacks));
    _options = options ?? throw new ArgumentNullException(nameof(options));
    _logger = (ILogger?)logger ?? NullLogger.Instance;
  }

  /// <summary>Reads the index and lists the gamepacks with their versions and the installed marker.</summary>
  /// <param name="ct">The cancellation token.</param>
  public async Task<IReadOnlyList<CatalogListItem>> ListAsync(CancellationToken ct = default) {
    try {
      var entries = await ReadEntriesAsync(_factory.Create(), ct).ConfigureAwait(false);
      var installed = _gamepacks.List().ToDictionary(g => g.Name, g => g.Version, GamepackNameRules.Comparer);
      var items = entries
        .GroupBy(e => e.Name, GamepackNameRules.Comparer)
        .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
        .Select(g => BuildItem(g.Key, g, installed.GetValueOrDefault(g.Key)))
        .ToList();
      LogList(_logger, "ok", items.Count);
      return items;
    }
    catch (CatalogException ex) {
      LogList(_logger, ex.Code, 0);
      throw;
    }
  }

  /// <summary>
  /// Installs one listed version, or checks it when <paramref name="dryRun"/> is true. The entry must be listed. This check
  /// ends before any file download (FR-010).
  /// </summary>
  /// <param name="name">The gamepack name.</param>
  /// <param name="version">The version.</param>
  /// <param name="dryRun">True to check only. No data changes.</param>
  /// <param name="ct">The cancellation token.</param>
  public async Task<GamepackInstallOutcome> InstallAsync(string name, string version, bool dryRun, CancellationToken ct = default) {
    try {
      var source = _factory.Create();
      var entries = await ReadEntriesAsync(source, ct).ConfigureAwait(false);
      var entry = FindEntry(entries, name, version)
        ?? throw new CatalogException(CatalogFailure.EntryNotListed, "The catalog does not list this gamepack version.");

      var file = await source.ReadFileAsync(entry.File, _options.FileLimitBytes, ct).ConfigureAwait(false);
      CheckHash(entry, file);
      string? signatureText = null;
      if (entry.Signature is not null) {
        var signature = await source.ReadFileAsync(entry.Signature, _options.SignatureLimitBytes, ct).ConfigureAwait(false);
        signatureText = ToText(signature);
      }

      var outcome = await _gamepacks.InstallAsync(ToText(file), dryRun, signatureText, ct).ConfigureAwait(false);
      LogInstall(_logger, entry.Name, entry.Version, dryRun, outcome.Status.ToString());
      return outcome;
    }
    catch (CatalogException ex) {
      LogInstall(_logger, name, version, dryRun, ex.Code);
      throw;
    }
  }

  private static async Task<IReadOnlyList<CatalogEntry>> ReadEntriesAsync(IGamepackCatalogSource source, CancellationToken ct) {
    var bytes = await source.ReadIndexAsync(ct).ConfigureAwait(false);
    return GamepackCatalogIndexParser.Parse(bytes);
  }

  private static CatalogEntry? FindEntry(IReadOnlyList<CatalogEntry> entries, string name, string version) {
    if (!CatalogVersion.TryParse(version, out var wanted)) {
      return null;
    }

    return entries.FirstOrDefault(e => GamepackNameRules.Comparer.Equals(e.Name, name) && e.ParsedVersion == wanted);
  }

  private static CatalogListItem BuildItem(string name, IEnumerable<CatalogEntry> entries, string? installed) {
    var hasInstalled = CatalogVersion.TryParse(installed, out var installedParsed);
    var versions = entries
      .OrderByDescending(e => e.ParsedVersion)
      .Select(e => {
        var isInstalled = installed is not null && (hasInstalled ? e.ParsedVersion == installedParsed : string.Equals(e.Version, installed, StringComparison.Ordinal));
        var newer = installed is null || (hasInstalled ? e.ParsedVersion > installedParsed : !isInstalled);
        return new CatalogVersionItem(e.Version, e.Changelog, isInstalled, newer);
      })
      .ToList();
    return new CatalogListItem(name, installed, versions);
  }

  private static void CheckHash(CatalogEntry entry, byte[] file) {
    if (entry.Sha256 is null) {
      return;
    }

    var expected = Convert.FromHexString(entry.Sha256);
    var actual = SHA256.HashData(file);
    if (!CryptographicOperations.FixedTimeEquals(expected, actual)) {
      throw new CatalogException(CatalogFailure.HashMismatch, "The SHA-256 of the file does not match the catalog index.");
    }
  }

  private static string ToText(byte[] bytes) {
    var offset = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF ? 3 : 0;
    return Encoding.UTF8.GetString(bytes, offset, bytes.Length - offset);
  }

  [LoggerMessage(EventId = 1471, Level = LogLevel.Information, Message = "The catalog list ended with the outcome {Outcome}. Gamepacks listed: {Count}.")]
  private static partial void LogList(ILogger logger, string outcome, int count);

  [LoggerMessage(EventId = 1472, Level = LogLevel.Information, Message = "The catalog install of {Name} {Version} ended with the outcome {Outcome}. Dry run: {DryRun}.")]
  private static partial void LogInstallCore(ILogger logger, string name, string version, string outcome, bool dryRun);

  private static void LogInstall(ILogger logger, string name, string version, bool dryRun, string outcome) =>
    // The request text is not trusted. The log shows only a valid name and a valid version.
    LogInstallCore(
      logger,
      GamepackNameRules.IsValid(name) ? name : "(not valid)",
      CatalogVersion.TryParse(version, out _) ? version : "(not valid)",
      outcome,
      dryRun);
}
