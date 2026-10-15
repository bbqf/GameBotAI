using System.Text.Json.Serialization;
using GameBot.Domain.Gamepacks;

namespace GameBot.Service.Endpoints.Dto;

/// <summary>
/// The owner of an object, as the object reads show it (feature 143): <c>{ "kind": "user" }</c> or
/// <c>{ "kind": "gamepack", "name": "...", "version": "..." }</c>. A write request ignores this field.
/// </summary>
internal sealed record GamepackOwnerDto {
  /// <summary><c>user</c> or <c>gamepack</c>.</summary>
  public required string Kind { get; init; }

  /// <summary>The gamepack name. Not set for the user.</summary>
  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public string? Name { get; init; }

  /// <summary>The gamepack version. Not set for the user.</summary>
  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public string? Version { get; init; }

  /// <summary>Makes the read shape of an owner.</summary>
  /// <param name="owner">The owner.</param>
  internal static GamepackOwnerDto From(GamepackOwner owner) {
    ArgumentNullException.ThrowIfNull(owner);
    return owner.IsUser
      ? new GamepackOwnerDto { Kind = "user" }
      : new GamepackOwnerDto { Kind = "gamepack", Name = owner.Name, Version = owner.Version };
  }
}

/// <summary>One problem in an install report.</summary>
internal sealed record GamepackProblemDto {
  /// <summary><c>structure</c>, <c>reference</c>, or <c>conflict</c>.</summary>
  public required string Category { get; init; }

  /// <summary><c>manifest</c>, <c>sequence</c>, <c>command</c>, or <c>image</c>.</summary>
  public required string ObjectType { get; init; }

  /// <summary>The object name, or null.</summary>
  public string? Name { get; init; }

  /// <summary>For a conflict: <c>user</c> or <c>&lt;name&gt; &lt;version&gt;</c>. Else null.</summary>
  public string? Owner { get; init; }

  /// <summary>The message.</summary>
  public required string Message { get; init; }

  internal static GamepackProblemDto From(GamepackProblem problem) {
    ArgumentNullException.ThrowIfNull(problem);
    return new GamepackProblemDto {
      Category = problem.Category,
      ObjectType = problem.ObjectType,
      Name = problem.Name,
      Owner = problem.Owner,
      Message = problem.Message
    };
  }
}

/// <summary>The name and version of a gamepack in a response.</summary>
internal sealed record GamepackRefDto(string? Name, string? Version);

/// <summary>The response of the install route (also for a dry run).</summary>
internal sealed record GamepackInstallResponse {
  /// <summary>True for a dry run.</summary>
  public bool DryRun { get; init; }

  /// <summary>True when the file has no problem.</summary>
  public bool Valid { get; init; }

  /// <summary>All problems.</summary>
  public IReadOnlyList<GamepackProblemDto> Problems { get; init; } = Array.Empty<GamepackProblemDto>();

  /// <summary>The gamepack name and version, when known.</summary>
  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public GamepackRefDto? Gamepack { get; init; }

  /// <summary>The number of created objects (planned for a dry run).</summary>
  public int Created { get; init; }

  /// <summary>The number of updated objects (planned for a dry run).</summary>
  public int Updated { get; init; }

  /// <summary>The number of removed objects (planned for a dry run).</summary>
  public int Removed { get; init; }
}

/// <summary>The response of a rollback that worked.</summary>
internal sealed record GamepackRollbackResponse {
  /// <summary>The gamepack name.</summary>
  public string? Name { get; init; }

  /// <summary>The version after the rollback.</summary>
  public string? Version { get; init; }

  /// <summary>Always <c>rolled_back</c>.</summary>
  public string Result { get; init; } = "rolled_back";

  /// <summary>The number of created objects.</summary>
  public int Created { get; init; }

  /// <summary>The number of updated objects.</summary>
  public int Updated { get; init; }

  /// <summary>The number of removed objects.</summary>
  public int Removed { get; init; }
}

/// <summary>The response of a refused rollback.</summary>
internal sealed record GamepackRollbackRefusedResponse {
  /// <summary>Always <c>refused</c>.</summary>
  public string Result { get; init; } = "refused";

  /// <summary>Always false.</summary>
  public bool Valid { get; init; }

  /// <summary>All problems.</summary>
  public IReadOnlyList<GamepackProblemDto> Problems { get; init; } = Array.Empty<GamepackProblemDto>();
}

/// <summary>One row of the installed gamepack list.</summary>
internal sealed record GamepackListItemDto {
  /// <summary>The gamepack name.</summary>
  public required string Name { get; init; }

  /// <summary>The version.</summary>
  public required string Version { get; init; }

  /// <summary>The change text.</summary>
  public required string Changelog { get; init; }

  /// <summary>The optional API major version.</summary>
  public int? RequiredApiMajor { get; init; }

  /// <summary>True when a previous version is kept.</summary>
  public bool HasPreviousVersion { get; init; }

  /// <summary>The time of the install.</summary>
  public DateTimeOffset InstalledAtUtc { get; init; }
}
