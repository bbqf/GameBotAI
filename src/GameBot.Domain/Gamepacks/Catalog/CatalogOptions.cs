namespace GameBot.Domain.Gamepacks.Catalog;

/// <summary>The limits of the catalog (feature 147). A test can set small values.</summary>
public sealed class CatalogOptions {
  /// <summary>The largest index in bytes (5 MB).</summary>
  public long IndexLimitBytes { get; set; } = 5L * 1024 * 1024;

  /// <summary>The largest gamepack file in bytes (100 MB).</summary>
  public long FileLimitBytes { get; set; } = 100L * 1024 * 1024;

  /// <summary>The largest signature file in bytes (1 MB).</summary>
  public long SignatureLimitBytes { get; set; } = 1024L * 1024;

  /// <summary>The time limit of each request.</summary>
  public TimeSpan RequestTimeout { get; set; } = TimeSpan.FromSeconds(30);

  /// <summary>The largest number of redirects that one request follows.</summary>
  public int MaxRedirects { get; set; } = 3;
}
