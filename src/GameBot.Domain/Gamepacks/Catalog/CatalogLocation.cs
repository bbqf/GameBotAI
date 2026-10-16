using System.Net;

namespace GameBot.Domain.Gamepacks.Catalog;

/// <summary>The kind of a catalog location.</summary>
public enum CatalogKind {
  /// <summary>A web address.</summary>
  Web,

  /// <summary>A full folder path.</summary>
  Folder
}

/// <summary>A checked catalog location.</summary>
/// <param name="Kind">The kind.</param>
/// <param name="WebBase">The web base address with a trailing slash. Null for a folder.</param>
/// <param name="Folder">The full folder path. Null for a web address.</param>
public sealed record ParsedCatalogLocation(CatalogKind Kind, Uri? WebBase, string? Folder);

/// <summary>
/// The rules for a catalog location (feature 147): the scheme rules, the base address, and the safe relative path rules.
/// A file location in the index must stay under the base (FR-011).
/// </summary>
public static class CatalogLocation {
  /// <summary>The name of the index document.</summary>
  public const string IndexName = "index.json";

  /// <summary>Checks a location. Gives false and a plain message when the location is not valid.</summary>
  /// <param name="location">The location text.</param>
  /// <param name="allowInsecure">True to allow <c>http</c> for a host that is not a loopback host.</param>
  /// <param name="parsed">The checked location.</param>
  /// <param name="error">The message when the location is not valid.</param>
  public static bool TryParse(string? location, bool allowInsecure, out ParsedCatalogLocation? parsed, out string error) {
    parsed = null;
    error = string.Empty;
    var text = location?.Trim();
    if (string.IsNullOrEmpty(text)) {
      error = "The catalog location is empty.";
      return false;
    }

    if (text.Contains("://", StringComparison.Ordinal)) {
      return TryParseWeb(text, allowInsecure, out parsed, out error);
    }

    if (!Path.IsPathFullyQualified(text)) {
      error = "The catalog location must be a web address or a full folder path.";
      return false;
    }

    parsed = new ParsedCatalogLocation(CatalogKind.Folder, null, Path.GetFullPath(text));
    return true;
  }

  /// <summary>True when the file location is a safe relative location (FR-011).</summary>
  /// <param name="path">The location from the index.</param>
  public static bool IsSafeRelativePath(string? path) {
    if (string.IsNullOrWhiteSpace(path) || path.Length > 1024 || !IsSafeText(path)) {
      return false;
    }

    var decoded = path;
    try {
      decoded = Uri.UnescapeDataString(path);
    }
    catch (UriFormatException) {
      return false;
    }

    return IsSafeText(decoded);
  }

  /// <summary>Resolves a file location against a web base. Gives null when the result leaves the base.</summary>
  /// <param name="baseUri">The base address with a trailing slash.</param>
  /// <param name="relative">The file location from the index.</param>
  public static Uri? ResolveWeb(Uri baseUri, string relative) {
    ArgumentNullException.ThrowIfNull(baseUri);
    if (!IsSafeRelativePath(relative) || !Uri.TryCreate(baseUri, relative, out var resolved)) {
      return null;
    }

    var sameOrigin = string.Equals(resolved.Scheme, baseUri.Scheme, StringComparison.OrdinalIgnoreCase)
      && string.Equals(resolved.Host, baseUri.Host, StringComparison.OrdinalIgnoreCase)
      && resolved.Port == baseUri.Port;
    return sameOrigin && resolved.AbsolutePath.StartsWith(baseUri.AbsolutePath, StringComparison.Ordinal) ? resolved : null;
  }

  /// <summary>
  /// Resolves a file location against a folder. Gives null when the result is not under the folder or when a part of the
  /// path below the folder is a link (a junction or a symbolic link).
  /// </summary>
  /// <param name="folder">The full folder path.</param>
  /// <param name="relative">The file location from the index.</param>
  public static string? ResolveFolder(string folder, string relative) {
    ArgumentException.ThrowIfNullOrEmpty(folder);
    if (!IsSafeRelativePath(relative) || Path.IsPathRooted(relative)) {
      return null;
    }

    var root = Path.GetFullPath(folder);
    var prefix = Path.EndsInDirectorySeparator(root) ? root : root + Path.DirectorySeparatorChar;
    var full = Path.GetFullPath(Path.Combine(root, relative));
    if (!full.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) {
      return null;
    }

    return ContainsLink(root, full) ? null : full;
  }

  /// <summary>True for a loopback host: localhost, 127.0.0.0/8, or ::1.</summary>
  /// <param name="uri">The address.</param>
  public static bool IsLoopback(Uri uri) {
    ArgumentNullException.ThrowIfNull(uri);
    if (string.Equals(uri.Host, "localhost", StringComparison.OrdinalIgnoreCase)) {
      return true;
    }

    return IPAddress.TryParse(uri.Host.Trim('[', ']'), out var address) && IPAddress.IsLoopback(address);
  }

  private static bool TryParseWeb(string text, bool allowInsecure, out ParsedCatalogLocation? parsed, out string error) {
    parsed = null;
    error = string.Empty;
    if (!Uri.TryCreate(text, UriKind.Absolute, out var uri)) {
      error = "The catalog web address is not valid.";
      return false;
    }

    var isHttps = string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase);
    var isHttp = string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase);
    if (!isHttps && !isHttp) {
      error = "The catalog web address must use https or http.";
      return false;
    }

    if (isHttp && !allowInsecure && !IsLoopback(uri)) {
      error = "The catalog web address must use https. Set allowInsecure to use http for this host.";
      return false;
    }

    if (!string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment)) {
      error = "The catalog web address must not have user information, a query, or a fragment.";
      return false;
    }

    var absolute = uri.AbsoluteUri;
    var withSlash = absolute.EndsWith('/') ? absolute : absolute + "/";
    parsed = new ParsedCatalogLocation(CatalogKind.Web, new Uri(withSlash, UriKind.Absolute), null);
    return true;
  }

  private static bool IsSafeText(string text) {
    if (text.Any(char.IsControl) || text.Contains('\\', StringComparison.Ordinal)
        || text.Contains('?', StringComparison.Ordinal) || text.Contains('#', StringComparison.Ordinal)
        || text.StartsWith('/') || text.Contains("//", StringComparison.Ordinal)) {
      return false;
    }

    var segments = text.Split('/');
    if (segments.Any(s => s is ".." or "." or "")) {
      return false;
    }

    // A colon in the first segment is a scheme or a drive letter.
    return !segments[0].Contains(':', StringComparison.Ordinal);
  }

  private static bool ContainsLink(string root, string full) {
    var current = full;
    while (current.Length > root.Length) {
      FileSystemInfo info = Directory.Exists(current) ? new DirectoryInfo(current) : new FileInfo(current);
      if (info.Exists && info.Attributes.HasFlag(FileAttributes.ReparsePoint)) {
        return true;
      }

      var parent = Path.GetDirectoryName(current);
      if (string.IsNullOrEmpty(parent)) {
        break;
      }

      current = parent;
    }

    return false;
  }
}
