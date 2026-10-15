using System.Text.RegularExpressions;

namespace GameBot.Domain.Gamepacks;

/// <summary>
/// The name rules for a gamepack (FR-019). A name has 1 to 64 characters from letters, digits, <c>_</c>, <c>-</c>,
/// and <c>.</c>. It does not start with <c>.</c>. It is not a reserved Windows device name. Names match without case.
/// </summary>
public static class GamepackNameRules {
  /// <summary>The largest name length.</summary>
  public const int MaxLength = 64;

  private static readonly Regex NamePattern = new("^[A-Za-z0-9_.-]{1,64}$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

  private static readonly HashSet<string> Reserved = BuildReserved();

  /// <summary>The comparer for gamepack names. It ignores case.</summary>
  public static StringComparer Comparer => StringComparer.OrdinalIgnoreCase;

  /// <summary>Checks a name. Returns null when the name is valid, else an STE message.</summary>
  /// <param name="name">The name to check.</param>
  public static string? Validate(string? name) {
    if (string.IsNullOrWhiteSpace(name)) {
      return "The gamepack name is required.";
    }

    if (name.Length > MaxLength) {
      return $"The gamepack name must have at most {MaxLength} characters.";
    }

    if (name[0] == '.') {
      return "The gamepack name must not start with a dot.";
    }

    if (!NamePattern.IsMatch(name)) {
      return "The gamepack name can have only letters, digits, underscore, hyphen, and dot.";
    }

    if (IsReserved(name)) {
      return "The gamepack name is a reserved device name.";
    }

    return null;
  }

  /// <summary>True when the name is valid.</summary>
  /// <param name="name">The name to check.</param>
  public static bool IsValid(string? name) => Validate(name) is null;

  /// <summary>True when the name, with or without an extension, is a reserved Windows device name.</summary>
  /// <param name="name">The name to check.</param>
  public static bool IsReserved(string name) {
    ArgumentNullException.ThrowIfNull(name);
    var dot = name.IndexOf('.', StringComparison.Ordinal);
    var stem = dot >= 0 ? name[..dot] : name;
    return Reserved.Contains(stem);
  }

  /// <summary>The lower-case form that is the registry key and the previous-file name.</summary>
  /// <param name="name">The gamepack name.</param>
  [System.Diagnostics.CodeAnalysis.SuppressMessage("Globalization", "CA1308:Normalize strings to uppercase", Justification = "The key is the lower-case name, as the data model says. It is never shown as text.")]
  public static string Key(string name) {
    ArgumentNullException.ThrowIfNull(name);
    return name.ToLowerInvariant();
  }

  private static HashSet<string> BuildReserved() {
    var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "CON", "PRN", "AUX", "NUL" };
    for (var i = 1; i <= 9; i++) {
      set.Add("COM" + i.ToString(System.Globalization.CultureInfo.InvariantCulture));
      set.Add("LPT" + i.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    return set;
  }
}
