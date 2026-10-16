using System.Globalization;

namespace GameBot.Domain.Gamepacks.Catalog;

/// <summary>A version number of a catalog entry: one to four numbers with dots. Missing parts count as 0.</summary>
public readonly record struct CatalogVersion : IComparable<CatalogVersion> {
  private CatalogVersion(int a, int b, int c, int d) {
    A = a;
    B = b;
    C = c;
    D = d;
  }

  private int A { get; }

  private int B { get; }

  private int C { get; }

  private int D { get; }

  /// <summary>Parses a version. Gives false for text that is not a version.</summary>
  /// <param name="text">The version text.</param>
  /// <param name="version">The version.</param>
  public static bool TryParse(string? text, out CatalogVersion version) {
    version = default;
    if (string.IsNullOrWhiteSpace(text)) {
      return false;
    }

    var parts = text.Trim().Split('.');
    if (parts.Length is < 1 or > 4) {
      return false;
    }

    var numbers = new int[4];
    for (var i = 0; i < parts.Length; i++) {
      if (parts[i].Length == 0 || !parts[i].All(char.IsAsciiDigit)
          || !int.TryParse(parts[i], NumberStyles.None, CultureInfo.InvariantCulture, out numbers[i])) {
        return false;
      }
    }

    version = new CatalogVersion(numbers[0], numbers[1], numbers[2], numbers[3]);
    return true;
  }

  /// <summary>Gives the four numbers as text. Two equal versions give the same text.</summary>
  public override string ToString() => string.Create(CultureInfo.InvariantCulture, $"{A}.{B}.{C}.{D}");

  /// <summary>Compares two versions.</summary>
  /// <param name="other">The other version.</param>
  public int CompareTo(CatalogVersion other) {
    var result = A.CompareTo(other.A);
    if (result != 0) {
      return result;
    }

    result = B.CompareTo(other.B);
    if (result != 0) {
      return result;
    }

    result = C.CompareTo(other.C);
    return result != 0 ? result : D.CompareTo(other.D);
  }

  /// <summary>True when the left version is older.</summary>
  /// <param name="left">The left version.</param>
  /// <param name="right">The right version.</param>
  public static bool operator <(CatalogVersion left, CatalogVersion right) => left.CompareTo(right) < 0;

  /// <summary>True when the left version is newer.</summary>
  /// <param name="left">The left version.</param>
  /// <param name="right">The right version.</param>
  public static bool operator >(CatalogVersion left, CatalogVersion right) => left.CompareTo(right) > 0;

  /// <summary>True when the left version is older or equal.</summary>
  /// <param name="left">The left version.</param>
  /// <param name="right">The right version.</param>
  public static bool operator <=(CatalogVersion left, CatalogVersion right) => left.CompareTo(right) <= 0;

  /// <summary>True when the left version is newer or equal.</summary>
  /// <param name="left">The left version.</param>
  /// <param name="right">The right version.</param>
  public static bool operator >=(CatalogVersion left, CatalogVersion right) => left.CompareTo(right) >= 0;
}

/// <summary>One version of one gamepack in the catalog index (feature 147).</summary>
/// <param name="Name">The gamepack name.</param>
/// <param name="Version">The version text, as in the index.</param>
/// <param name="ParsedVersion">The parsed version.</param>
/// <param name="Changelog">The change text. Empty when the index has none.</param>
/// <param name="File">The relative location of the gamepack file.</param>
/// <param name="Signature">The relative location of the detached signature file, or null.</param>
/// <param name="Sha256">The SHA-256 of the gamepack file in hex, or null.</param>
public sealed record CatalogEntry(
    string Name,
    string Version,
    CatalogVersion ParsedVersion,
    string Changelog,
    string File,
    string? Signature,
    string? Sha256);
