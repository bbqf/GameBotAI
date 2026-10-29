namespace GameBot.Domain.Parameters;

/// <summary>
/// One option of a choice parameter (feature 168): a readable label for the person and the technical
/// value that the sequence receives.
/// </summary>
public sealed class ParameterOption {
  /// <summary>The text that the person sees. Unique in the list, without case.</summary>
  public required string Label { get; init; }

  /// <summary>The technical value that the commands receive. Unique in the list.</summary>
  public required string Value { get; init; }
}
