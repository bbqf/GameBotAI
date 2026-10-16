using GameBot.Domain.Gamepacks.Managed;

namespace GameBot.Service.Endpoints.Dto;

/// <summary>
/// The route values of a managed call (feature 148): the <c>dryRun</c> flag, the repeatable <c>templates</c> values, and the
/// <c>managed</c> flag of the rollback route. The two update routes and the rollback route use the same mapping.
/// </summary>
internal sealed record ManagedUpdateQuery {
  /// <summary>True for a dry run. The default is false.</summary>
  public bool DryRun { get; init; }

  /// <summary>The template names, or null for all templates. Blank names are dropped.</summary>
  public IReadOnlyList<string>? Templates { get; init; }

  /// <summary>True for a managed rollback. The default is false.</summary>
  public bool Managed { get; init; }

  /// <summary>Builds the values from the bound route parameters.</summary>
  /// <param name="dryRun">The <c>dryRun</c> value, or null.</param>
  /// <param name="templates">The repeated <c>templates</c> values, or null.</param>
  /// <param name="managed">The <c>managed</c> value, or null.</param>
  internal static ManagedUpdateQuery From(bool? dryRun, IEnumerable<string>? templates, bool? managed = null) {
    var names = templates?.Where(n => !string.IsNullOrWhiteSpace(n)).Select(n => n.Trim()).ToList();
    return new ManagedUpdateQuery {
      DryRun = dryRun ?? false,
      Templates = names is { Count: > 0 } ? names : null,
      Managed = managed ?? false
    };
  }

  /// <summary>Makes a request for an update with a file text.</summary>
  /// <param name="fileText">The gamepack file text.</param>
  /// <param name="signatureText">The signature text, or null.</param>
  internal ManagedUpdateRequest ToUpdateRequest(string fileText, string? signatureText) => new() {
    Mode = ManagedUpdateMode.Update,
    FileText = fileText,
    SignatureText = signatureText,
    DryRun = DryRun,
    Templates = Templates
  };

  /// <summary>Makes a request for a managed rollback.</summary>
  /// <param name="name">The gamepack name.</param>
  internal ManagedUpdateRequest ToRollbackRequest(string name) => new() {
    Mode = ManagedUpdateMode.Rollback,
    GamepackName = name,
    DryRun = DryRun,
    Templates = Templates
  };
}

/// <summary>The body of the catalog update route.</summary>
internal sealed record GamepackCatalogUpdateRequest {
  /// <summary>The gamepack name. It is required.</summary>
  public string? Name { get; init; }

  /// <summary>The version. It is required.</summary>
  public string? Version { get; init; }

  /// <summary>True to check only. No data changes.</summary>
  public bool DryRun { get; init; }

  /// <summary>The template names that limit the call. Null or empty means all templates.</summary>
  public IReadOnlyList<string>? Templates { get; init; }
}
