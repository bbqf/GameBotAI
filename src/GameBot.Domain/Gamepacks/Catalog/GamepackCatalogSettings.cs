namespace GameBot.Domain.Gamepacks.Catalog;

/// <summary>The catalog settings (feature 147). The file is <c>gamepack-catalog.json</c> in the data root.</summary>
public sealed class GamepackCatalogSettings {
  /// <summary>A web address or a full folder path. Null or empty means not configured.</summary>
  public string? Location { get; set; }

  /// <summary>True to allow <c>http</c> for a host that is not a loopback host. Web addresses only.</summary>
  public bool AllowInsecure { get; set; }

  /// <summary>The secret bearer token. Never returned and never logged. Web addresses only.</summary>
  public string? Token { get; set; }
}
