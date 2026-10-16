namespace GameBot.Domain.Gamepacks.Catalog;

/// <summary>
/// Picks the catalog source from the current settings on each request (feature 147, FR-003). A settings change needs no
/// restart. The factory holds no source and no cache.
/// </summary>
public sealed class GamepackCatalogSourceFactory {
  private readonly IGamepackCatalogSettingsStore _settings;
  private readonly Func<HttpClient> _httpClient;
  private readonly CatalogOptions _options;

  /// <summary>Creates the factory.</summary>
  /// <param name="settings">The settings store.</param>
  /// <param name="httpClient">Makes an HTTP client with redirects off.</param>
  /// <param name="options">The limits.</param>
  public GamepackCatalogSourceFactory(IGamepackCatalogSettingsStore settings, Func<HttpClient> httpClient, CatalogOptions options) {
    _settings = settings ?? throw new ArgumentNullException(nameof(settings));
    _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
    _options = options ?? throw new ArgumentNullException(nameof(options));
  }

  /// <summary>Makes the source for the current settings.</summary>
  public IGamepackCatalogSource Create() {
    var settings = _settings.Load();
    if (string.IsNullOrWhiteSpace(settings.Location)) {
      throw new CatalogException(CatalogFailure.NotConfigured, "No catalog location is set.");
    }

    if (!CatalogLocation.TryParse(settings.Location, settings.AllowInsecure, out var parsed, out var error) || parsed is null) {
      throw new CatalogException(CatalogFailure.InvalidLocation, error);
    }

    return parsed.Kind == CatalogKind.Folder
      ? new FolderCatalogSource(parsed.Folder!, _options)
      : new HttpCatalogSource(_httpClient(), parsed.WebBase!, settings.Token, _options);
  }
}
