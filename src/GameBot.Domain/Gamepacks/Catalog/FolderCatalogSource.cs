namespace GameBot.Domain.Gamepacks.Catalog;

/// <summary>
/// A catalog in a local folder (feature 147, FR-006). Each read is bound to the folder: a location that leaves the folder, or
/// that goes through a link, is refused. The token and the allowInsecure flag do not apply here.
/// </summary>
public sealed class FolderCatalogSource : IGamepackCatalogSource {
  private readonly string _folder;
  private readonly CatalogOptions _options;

  /// <summary>Creates the source.</summary>
  /// <param name="folder">The full folder path.</param>
  /// <param name="options">The limits.</param>
  public FolderCatalogSource(string folder, CatalogOptions options) {
    ArgumentException.ThrowIfNullOrEmpty(folder);
    _folder = folder;
    _options = options ?? throw new ArgumentNullException(nameof(options));
  }

  /// <inheritdoc />
  public async Task<byte[]> ReadIndexAsync(CancellationToken ct) {
    var path = CatalogLocation.ResolveFolder(_folder, CatalogLocation.IndexName);
    if (path is null || !File.Exists(path)) {
      throw new CatalogException(CatalogFailure.IndexInvalid, "The catalog folder has no index.json.");
    }

    return await ReadAsync(path, _options.IndexLimitBytes, CatalogFailure.IndexTooLarge, ct).ConfigureAwait(false);
  }

  /// <inheritdoc />
  public async Task<byte[]> ReadFileAsync(string location, long limitBytes, CancellationToken ct) {
    var path = CatalogLocation.ResolveFolder(_folder, location);
    if (path is null) {
      throw new CatalogException(CatalogFailure.FileMissing, "The file location is not under the catalog folder.");
    }

    if (!File.Exists(path)) {
      throw new CatalogException(CatalogFailure.FileMissing, "The catalog folder has no file at the listed location.");
    }

    return await ReadAsync(path, limitBytes, CatalogFailure.FileTooLarge, ct).ConfigureAwait(false);
  }

  private static async Task<byte[]> ReadAsync(string path, long limit, string tooLargeCode, CancellationToken ct) {
    try {
      using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, useAsync: true);
      if (stream.Length > limit) {
        throw new CatalogException(tooLargeCode, "The file is larger than the size limit.");
      }

      using var buffer = new MemoryStream();
      var chunk = new byte[81920];
      int read;
      while ((read = await stream.ReadAsync(chunk, ct).ConfigureAwait(false)) > 0) {
        if (buffer.Length + read > limit) {
          throw new CatalogException(tooLargeCode, "The file is larger than the size limit.");
        }

        await buffer.WriteAsync(chunk.AsMemory(0, read), ct).ConfigureAwait(false);
      }

      return buffer.ToArray();
    }
    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) {
      throw new CatalogException(CatalogFailure.Unreachable, "The catalog folder cannot be read.", ex);
    }
  }
}
