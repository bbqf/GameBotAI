using GameBot.Domain.QueueTemplates;

namespace GameBot.Domain.Gamepacks.Seeds;

/// <summary>The outcome names of one seed in the plan.</summary>
public static class SeedOutcomes {
  /// <summary>The install creates the template.</summary>
  public const string Create = "create";

  /// <summary>A template with this name exists. The install does not create the seed.</summary>
  public const string SkipExists = "skip_exists";
}

/// <summary>The reasons why a seed is not created. The value is a string, so a new reason needs no schema change.</summary>
public static class SeedSkipReasons {
  /// <summary>A template with the same name existed at plan time or at write time.</summary>
  public const string Exists = "skipped_exists";
}

/// <summary>One seed template in the plan.</summary>
public sealed class SeedPlanItem {
  /// <summary>The template name from the file.</summary>
  public required string Name { get; init; }

  /// <summary>One of <see cref="SeedOutcomes"/>.</summary>
  public required string Outcome { get; init; }

  /// <summary>The new template id. Set only when the outcome is <see cref="SeedOutcomes.Create"/>.</summary>
  public string? TemplateId { get; init; }

  /// <summary>The entries with sequence ids. Set only when the outcome is <see cref="SeedOutcomes.Create"/>.</summary>
  public IReadOnlyList<QueueTemplateEntry> Entries { get; init; } = Array.Empty<QueueTemplateEntry>();
}

/// <summary>The seed templates of an install, in file order. The list is empty when the install is not a first install.</summary>
public sealed class SeedPlan {
  /// <summary>A plan with no seed.</summary>
  public static SeedPlan Empty { get; } = new();

  /// <summary>One item for each seed.</summary>
  public IReadOnlyList<SeedPlanItem> Items { get; init; } = Array.Empty<SeedPlanItem>();
}
