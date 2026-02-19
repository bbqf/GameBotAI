namespace GameBot.Service.Models;

internal sealed class VersionResolveRequestModel
{
  public required string BuildContext { get; init; }
  public VersionOverrideModel? Override { get; init; }
  public ReleaseLineMarkerModel? ReleaseLineMarker { get; init; }
  public CiBuildCounterModel? CiBuildCounter { get; init; }

  // Backward-compatible flat fields
  public int? Major { get; init; }
  public int? Minor { get; init; }
  public int? Patch { get; init; }
  public string? ReleaseLineId { get; init; }
  public int? ReleaseLineSequence { get; init; }
  public int? LastCiBuild { get; init; }
}

internal sealed class VersionOverrideModel
{
  public int? Major { get; init; }
  public int? Minor { get; init; }
  public int? Patch { get; init; }
}

internal sealed class ReleaseLineMarkerModel
{
  public string? ReleaseLineId { get; init; }
  public int? Sequence { get; init; }
}

internal sealed class CiBuildCounterModel
{
  public int? LastBuild { get; init; }
}

internal sealed class SemanticVersionModel
{
  public required int Major { get; init; }
  public required int Minor { get; init; }
  public required int Patch { get; init; }
  public required int Build { get; init; }
}

internal sealed class VersionResolveResultModel
{
  public required SemanticVersionModel Version { get; init; }
  public required string Source { get; init; }
  public required bool Persisted { get; init; }
  public required bool Authoritative { get; init; }
  public required string[] Notes { get; init; }
}

internal sealed class InstallCompareRequestModel
{
  public required SemanticVersionModel InstalledVersion { get; init; }
  public required SemanticVersionModel CandidateVersion { get; init; }
}

internal sealed class InstallCompareResultModel
{
  public required string Outcome { get; init; }
  public required string Reason { get; init; }
  public required bool PreserveProperties { get; init; }
}

internal sealed class SameBuildDecisionRequestModel
{
  public required string Mode { get; init; }
  public string? InteractiveChoice { get; init; }
}

internal sealed class SameBuildDecisionResultModel
{
  public required string Action { get; init; }
  public required bool MutatesState { get; init; }
  public required int StatusCode { get; init; }
}
