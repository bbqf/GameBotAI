namespace GameBot.Service.Endpoints.Dto;

/// <summary>The catalog settings in a response. The token is never in the body.</summary>
internal sealed record GamepackCatalogSettingsDto {
  /// <summary>The web address or the folder path, or null.</summary>
  public string? Location { get; init; }

  /// <summary>True when <c>http</c> is allowed for a host that is not a loopback host.</summary>
  public bool AllowInsecure { get; init; }

  /// <summary>True when a token is saved.</summary>
  public bool TokenSet { get; init; }
}

/// <summary>The request to change the catalog settings.</summary>
internal sealed record GamepackCatalogSettingsRequest {
  /// <summary>The web address or the folder path. Null or empty clears it.</summary>
  public string? Location { get; init; }

  /// <summary>True to allow <c>http</c> for a host that is not a loopback host.</summary>
  public bool AllowInsecure { get; init; }

  /// <summary>Null keeps the saved token. An empty text clears it. Other text sets it.</summary>
  public string? Token { get; init; }
}

/// <summary>One version in the catalog list.</summary>
internal sealed record GamepackCatalogVersionDto {
  /// <summary>The version.</summary>
  public required string Version { get; init; }

  /// <summary>The change text.</summary>
  public required string Changelog { get; init; }

  /// <summary>True for the installed version.</summary>
  public bool Installed { get; init; }

  /// <summary>True when no version is installed or this version is newer than the installed one.</summary>
  public bool UpdateAvailable { get; init; }
}

/// <summary>One gamepack in the catalog list.</summary>
internal sealed record GamepackCatalogItemDto {
  /// <summary>The gamepack name.</summary>
  public required string Name { get; init; }

  /// <summary>The installed version, or null.</summary>
  public string? InstalledVersion { get; init; }

  /// <summary>The versions from new to old.</summary>
  public required IReadOnlyList<GamepackCatalogVersionDto> Versions { get; init; }
}

/// <summary>The catalog list response.</summary>
internal sealed record GamepackCatalogListResponse {
  /// <summary>The gamepacks, ordered by name.</summary>
  public required IReadOnlyList<GamepackCatalogItemDto> Gamepacks { get; init; }
}

/// <summary>The request to install one version from the catalog.</summary>
internal sealed record GamepackCatalogInstallRequest {
  /// <summary>The gamepack name.</summary>
  public string? Name { get; init; }

  /// <summary>The version.</summary>
  public string? Version { get; init; }

  /// <summary>True to check only. No data changes.</summary>
  public bool DryRun { get; init; }
}
