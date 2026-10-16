namespace GameBot.Domain.Gamepacks;

/// <summary>The object types of a gamepack. The values are used in keys and in problem reports.</summary>
public static class GamepackObjectTypes {
  /// <summary>A manifest problem.</summary>
  public const string Manifest = "manifest";

  /// <summary>A sequence.</summary>
  public const string Sequence = "sequence";

  /// <summary>A command.</summary>
  public const string Command = "command";

  /// <summary>An image.</summary>
  public const string Image = "image";
}

/// <summary>The problem categories.</summary>
public static class GamepackProblemCategories {
  /// <summary>The file has a wrong shape or value.</summary>
  public const string Structure = "structure";

  /// <summary>A reference target is missing.</summary>
  public const string Reference = "reference";

  /// <summary>An object of the file clashes with an object that the gamepack does not own.</summary>
  public const string Conflict = "conflict";

  /// <summary>The signature check rejected the gamepack (feature 146).</summary>
  public const string Signature = "signature";
}

/// <summary>One problem that the check found.</summary>
/// <param name="Category">One of <see cref="GamepackProblemCategories"/>.</param>
/// <param name="ObjectType">One of <see cref="GamepackObjectTypes"/>.</param>
/// <param name="Name">The object name, or null.</param>
/// <param name="Message">The STE message.</param>
/// <param name="Owner">For a conflict only: <c>user</c> or <c>&lt;name&gt; &lt;version&gt;</c>.</param>
/// <param name="Code">For a signature problem only: <c>signature_&lt;status&gt;</c>.</param>
public sealed record GamepackProblem(string Category, string ObjectType, string? Name, string Message, string? Owner = null, string? Code = null);
