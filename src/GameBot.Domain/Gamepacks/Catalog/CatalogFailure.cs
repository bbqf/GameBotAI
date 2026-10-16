namespace GameBot.Domain.Gamepacks.Catalog;

/// <summary>The reason codes of a catalog failure (feature 147). A code is part of the API contract.</summary>
public static class CatalogFailure {
  /// <summary>No catalog location is set.</summary>
  public const string NotConfigured = "not_configured";

  /// <summary>The catalog location is not valid.</summary>
  public const string InvalidLocation = "invalid_location";

  /// <summary>The catalog cannot be reached.</summary>
  public const string Unreachable = "unreachable";

  /// <summary>The catalog refused the token (HTTP 401).</summary>
  public const string Unauthorized = "unauthorized";

  /// <summary>The catalog denied access (HTTP 403).</summary>
  public const string Forbidden = "forbidden";

  /// <summary>The catalog answered "not found" for a listed file.</summary>
  public const string NotFound = "not_found";

  /// <summary>The index is missing or not valid.</summary>
  public const string IndexInvalid = "index_invalid";

  /// <summary>The index is larger than the limit.</summary>
  public const string IndexTooLarge = "index_too_large";

  /// <summary>A listed file is missing in a folder catalog.</summary>
  public const string FileMissing = "file_missing";

  /// <summary>A file is larger than the limit.</summary>
  public const string FileTooLarge = "file_too_large";

  /// <summary>The SHA-256 of a file does not match the index.</summary>
  public const string HashMismatch = "hash_mismatch";

  /// <summary>A redirect was refused.</summary>
  public const string RedirectRefused = "redirect_refused";

  /// <summary>A request took more time than the limit.</summary>
  public const string Timeout = "timeout";

  /// <summary>The index does not list the name or version. Always checked before any download.</summary>
  public const string EntryNotListed = "entry_not_listed";
}

/// <summary>A catalog failure with a reason code. The message never holds the token.</summary>
public sealed class CatalogException : Exception {
  /// <summary>Creates the exception.</summary>
  public CatalogException() : this(CatalogFailure.Unreachable, "The catalog failed.") { }

  /// <summary>Creates the exception.</summary>
  /// <param name="message">The message.</param>
  public CatalogException(string message) : this(CatalogFailure.Unreachable, message) { }

  /// <summary>Creates the exception.</summary>
  /// <param name="message">The message.</param>
  /// <param name="innerException">The inner exception.</param>
  public CatalogException(string message, Exception innerException) : base(message, innerException) {
    Code = CatalogFailure.Unreachable;
  }

  /// <summary>Creates the exception.</summary>
  /// <param name="code">One of the <see cref="CatalogFailure"/> codes.</param>
  /// <param name="message">The message in plain words.</param>
  public CatalogException(string code, string message) : base(message) {
    Code = code;
  }

  /// <summary>Creates the exception.</summary>
  /// <param name="code">One of the <see cref="CatalogFailure"/> codes.</param>
  /// <param name="message">The message in plain words.</param>
  /// <param name="innerException">The inner exception.</param>
  public CatalogException(string code, string message, Exception innerException) : base(message, innerException) {
    Code = code;
  }

  /// <summary>The reason code.</summary>
  public string Code { get; } = CatalogFailure.Unreachable;
}
