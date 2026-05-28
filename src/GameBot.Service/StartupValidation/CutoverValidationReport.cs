namespace GameBot.Service.StartupValidation;

internal sealed record CutoverValidationReport {
  public required DateTime CheckedAtUtc { get; init; }
  public required IReadOnlyList<CutoverValidationIssue> Issues { get; init; } = Array.Empty<CutoverValidationIssue>();

  public bool IsBlocked => Issues.Count > 0;

  public static CutoverValidationReport Clean(DateTime checkedAtUtc) => new() {
    CheckedAtUtc = checkedAtUtc,
    Issues = Array.Empty<CutoverValidationIssue>()
  };

  public static CutoverValidationReport Blocked(DateTime checkedAtUtc, IReadOnlyList<CutoverValidationIssue> issues) => new() {
    CheckedAtUtc = checkedAtUtc,
    Issues = issues
  };
}

internal sealed record CutoverValidationIssue {
  public required string Store { get; init; }
  public required string Path { get; init; }
  public required string Message { get; init; }
  public required int ReferenceCount { get; init; }
}
