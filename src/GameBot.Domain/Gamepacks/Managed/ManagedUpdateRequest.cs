namespace GameBot.Domain.Gamepacks.Managed;

/// <summary>The mode of a managed call.</summary>
public enum ManagedUpdateMode {
  /// <summary>Install a gamepack file. The install step is an install.</summary>
  Update,

  /// <summary>Roll a gamepack back. The install step is the rollback.</summary>
  Rollback
}

/// <summary>One managed update request (feature 148). It lives for one call.</summary>
public sealed class ManagedUpdateRequest {
  /// <summary>The mode. <see cref="ManagedUpdateMode.Rollback"/> comes from <c>managed=true</c> on the rollback route.</summary>
  public ManagedUpdateMode Mode { get; init; } = ManagedUpdateMode.Update;

  /// <summary>The gamepack file text. Set for an update. Null for a rollback.</summary>
  public string? FileText { get; init; }

  /// <summary>The detached signature text, or null.</summary>
  public string? SignatureText { get; init; }

  /// <summary>The catalog name, when the source is the catalog. The route turns it into <see cref="FileText"/>.</summary>
  public string? CatalogName { get; init; }

  /// <summary>The catalog version, when the source is the catalog.</summary>
  public string? CatalogVersion { get; init; }

  /// <summary>The gamepack name. For a rollback it comes from the route. For an update the flow reads it from the file.</summary>
  public string? GamepackName { get; init; }

  /// <summary>True: no stop, no backup, no install, and no template change.</summary>
  public bool DryRun { get; init; }

  /// <summary>The template names that limit the call. Null or empty means all templates. Names match without case.</summary>
  public IReadOnlyList<string>? Templates { get; init; }
}
