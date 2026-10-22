namespace GameBot.Domain.Gamepacks;

/// <summary>The explanation of one problem: if take-over resolves it, and what to do.</summary>
/// <param name="TakeOverResolves">True if an install with take-over removes this problem.</param>
/// <param name="Remedy">The STE remedy text.</param>
public sealed record GamepackProblemExplanation(bool TakeOverResolves, string Remedy);

/// <summary>One line of the failure summary: all problems of one category.</summary>
/// <param name="Category">One of <see cref="GamepackProblemCategories"/>.</param>
/// <param name="Count">The number of problems. At least 1.</param>
/// <param name="Label">The label. It uses the singular form when the count is 1.</param>
/// <param name="Remedy">The STE remedy for the entry.</param>
/// <param name="ResolvedByTakeOver">True only if take-over resolves every problem of the entry.</param>
public sealed record GamepackSummaryEntry(string Category, int Count, string Label, string Remedy, bool ResolvedByTakeOver);

/// <summary>The failure summary of an install check.</summary>
/// <param name="Headline">One line with the counts.</param>
/// <param name="Entries">One entry for each category that has a problem.</param>
public sealed record GamepackFailureSummary(string Headline, IReadOnlyList<GamepackSummaryEntry> Entries);

/// <summary>
/// Explains the problems of an install check (feature 161). The class is pure: it reads the problems and
/// does not change any data. The rules are the same for every route that shows a problem.
/// </summary>
public static class GamepackFailureExplainer {
  private const string NoProblemsHeadline = "No problems.";

  // The order of the entries in a summary.
  private static readonly string[] CategoryOrder = {
    GamepackProblemCategories.Structure,
    GamepackProblemCategories.Signature,
    GamepackProblemCategories.Reference,
    GamepackProblemCategories.Conflict
  };

  private enum ConflictKind { User, OtherGamepack, Coded }

  /// <summary>Explains one problem.</summary>
  /// <param name="problem">The problem.</param>
  /// <param name="takeOverOn">True if the request has take-over on.</param>
  public static GamepackProblemExplanation Explain(GamepackProblem problem, bool takeOverOn) {
    ArgumentNullException.ThrowIfNull(problem);
    switch (problem.Category) {
      case GamepackProblemCategories.Structure:
        return new GamepackProblemExplanation(false, "Correct the gamepack file. Take-over cannot resolve this.");
      case GamepackProblemCategories.Signature:
        return new GamepackProblemExplanation(false, "Sign the file with a trusted key, or change the signature policy. Take-over cannot resolve this.");
      case GamepackProblemCategories.Reference:
        return new GamepackProblemExplanation(false, "Add the missing object to the file, or install it on the system first. Take-over cannot resolve this.");
      case GamepackProblemCategories.Conflict:
        return ExplainConflict(problem, takeOverOn);
      default:
        return new GamepackProblemExplanation(false, "Correct the gamepack file. Take-over cannot resolve this.");
    }
  }

  /// <summary>Makes the summary of a list of problems.</summary>
  /// <param name="problems">The problems.</param>
  /// <param name="takeOverOn">True if the request has take-over on.</param>
  public static GamepackFailureSummary Summarize(IReadOnlyList<GamepackProblem> problems, bool takeOverOn) {
    ArgumentNullException.ThrowIfNull(problems);
    if (problems.Count == 0) {
      return new GamepackFailureSummary(NoProblemsHeadline, Array.Empty<GamepackSummaryEntry>());
    }

    var entries = new List<GamepackSummaryEntry>();
    var headlineParts = new List<string>();
    foreach (var category in CategoryOrder) {
      var group = problems.Where(p => p.Category == category).ToList();
      if (group.Count > 0) {
        entries.Add(BuildEntry(category, group, takeOverOn));
        headlineParts.Add($"{group.Count} {CategoryNoun(category, group.Count)}");
      }
    }

    // A problem with an unknown category still counts. It uses the structure rules.
    var other = problems.Where(p => !CategoryOrder.Contains(p.Category)).ToList();
    if (other.Count > 0) {
      entries.Add(BuildEntry(GamepackProblemCategories.Structure, other, takeOverOn));
      headlineParts.Add($"{other.Count} {CategoryNoun(GamepackProblemCategories.Structure, other.Count)}");
    }

    return new GamepackFailureSummary(string.Join(", ", headlineParts) + ".", entries);
  }

  private static GamepackProblemExplanation ExplainConflict(GamepackProblem problem, bool takeOverOn) =>
    ConflictKindOf(problem) switch {
      ConflictKind.Coded => new GamepackProblemExplanation(false, "Try again when the other call ends. Take-over cannot resolve this."),
      ConflictKind.User when !takeOverOn => new GamepackProblemExplanation(true, "Install with take-over to replace it, or rename or delete it."),
      ConflictKind.User => new GamepackProblemExplanation(false, "Rename or delete the object. Take-over is on and cannot resolve this."),
      _ => new GamepackProblemExplanation(false, "Rename the object in the file, or remove the other gamepack. Take-over cannot resolve this.")
    };

  private static ConflictKind ConflictKindOf(GamepackProblem problem) {
    if (!string.IsNullOrEmpty(problem.Code)) {
      return ConflictKind.Coded;
    }

    return string.Equals(problem.Owner, "user", StringComparison.Ordinal) ? ConflictKind.User : ConflictKind.OtherGamepack;
  }

  private static GamepackSummaryEntry BuildEntry(string category, List<GamepackProblem> group, bool takeOverOn) {
    var count = group.Count;
    var resolved = group.All(p => Explain(p, takeOverOn).TakeOverResolves);
    if (category != GamepackProblemCategories.Conflict) {
      return new GamepackSummaryEntry(category, count, CategoryLabel(category, count), EntryRemedy(category), resolved);
    }

    // A conflict entry names each part when the owners differ.
    var kinds = group.Select(ConflictKindOf).Distinct().OrderBy(k => k).ToList();
    var plural = count != 1;
    var label = kinds.Count == 1
      ? ConflictLabel(kinds[0], plural, takeOverOn)
      : (plural ? "conflicts: " : "conflict: ") + string.Join("; ", kinds.Select(k => ConflictPart(k, takeOverOn)));
    var remedy = string.Join(" ", kinds.Select(k => ConflictRemedy(k, takeOverOn)));
    return new GamepackSummaryEntry(category, count, label, remedy, resolved);
  }

  private static string CategoryNoun(string category, int count) => category switch {
    GamepackProblemCategories.Structure => count == 1 ? "structure problem" : "structure problems",
    GamepackProblemCategories.Signature => count == 1 ? "signature problem" : "signature problems",
    GamepackProblemCategories.Reference => count == 1 ? "reference problem" : "reference problems",
    _ => count == 1 ? "conflict" : "conflicts"
  };

  private static string CategoryLabel(string category, int count) => category switch {
    GamepackProblemCategories.Structure => count == 1 ? "structure problem: the file is not correct" : "structure problems: the file is not correct",
    GamepackProblemCategories.Signature => count == 1 ? "signature problem: the signature check rejected the file" : "signature problems: the signature check rejected the file",
    GamepackProblemCategories.Reference => count == 1 ? "reference problem: a target object is missing" : "reference problems: target objects are missing",
    _ => count == 1 ? "problem" : "problems"
  };

  private static string EntryRemedy(string category) => category switch {
    GamepackProblemCategories.Signature => "Sign the file with a trusted key, or change the signature policy. Take-over cannot resolve this.",
    GamepackProblemCategories.Reference => "Add the missing objects to the file, or install them on the system first. Take-over cannot resolve this.",
    _ => "Correct the gamepack file. Take-over cannot resolve this."
  };

  private static string ConflictLabel(ConflictKind kind, bool plural, bool takeOverOn) =>
    (plural ? "conflicts: " : "conflict: ") + ConflictPart(kind, takeOverOn);

  private static string ConflictPart(ConflictKind kind, bool takeOverOn) => kind switch {
    ConflictKind.User when takeOverOn => "objects with the same name as another object of the user",
    ConflictKind.User => "objects owned by the user",
    ConflictKind.OtherGamepack => "objects owned by another gamepack",
    _ => "another install or update is in progress"
  };

  private static string ConflictRemedy(ConflictKind kind, bool takeOverOn) => kind switch {
    ConflictKind.User when takeOverOn => "Rename or delete the objects. Take-over is on and cannot resolve this.",
    ConflictKind.User => "Install with take-over to replace them, or rename or delete them.",
    ConflictKind.OtherGamepack => "Rename the objects in the file, or remove the other gamepack. Take-over cannot resolve this.",
    _ => "Try again when the other call ends. Take-over cannot resolve this."
  };
}
