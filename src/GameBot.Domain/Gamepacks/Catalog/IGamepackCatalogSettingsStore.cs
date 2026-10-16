namespace GameBot.Domain.Gamepacks.Catalog;

/// <summary>The store of the catalog settings (feature 147). A read happens on each call, so a change needs no restart.</summary>
public interface IGamepackCatalogSettingsStore {
  /// <summary>Reads the settings. A missing or damaged file gives empty settings.</summary>
  GamepackCatalogSettings Load();

  /// <summary>
  /// Saves the settings. Token: null keeps the saved token, an empty text clears it, other text sets it.
  /// Throws <see cref="CatalogException"/> with <c>invalid_location</c> when the location is not valid.
  /// </summary>
  /// <param name="location">The location. Null or empty clears it.</param>
  /// <param name="allowInsecure">True to allow <c>http</c> for a host that is not a loopback host.</param>
  /// <param name="token">The token change.</param>
  GamepackCatalogSettings Save(string? location, bool allowInsecure, string? token);
}
