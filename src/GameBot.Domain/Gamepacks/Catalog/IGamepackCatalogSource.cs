namespace GameBot.Domain.Gamepacks.Catalog;

/// <summary>A source of catalog data: a web catalog or a folder catalog (feature 147).</summary>
public interface IGamepackCatalogSource {
  /// <summary>Reads <c>index.json</c>. A missing index gives <c>index_invalid</c>.</summary>
  /// <param name="ct">The cancellation token.</param>
  Task<byte[]> ReadIndexAsync(CancellationToken ct);

  /// <summary>
  /// Reads a file (a gamepack file or a signature file) at a relative location. The size limit differs for each use.
  /// </summary>
  /// <param name="location">The relative location from the index.</param>
  /// <param name="limitBytes">The largest size in bytes.</param>
  /// <param name="ct">The cancellation token.</param>
  Task<byte[]> ReadFileAsync(string location, long limitBytes, CancellationToken ct);
}
