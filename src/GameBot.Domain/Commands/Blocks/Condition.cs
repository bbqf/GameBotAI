namespace GameBot.Domain.Commands.Blocks;

public sealed class Condition {
  public required string Source { get; init; } // image | text | trigger
  public required string TargetId { get; init; }
  public required string Mode { get; init; } // Present | Absent
  public double? ConfidenceThreshold { get; init; }
  public Rect? Region { get; init; }
  /// <summary>Optional search area in capture pixels (feature 130). Wins over the fraction <see cref="Region"/>.</summary>
  public PixelRegion? PixelRegion { get; init; }
  public string? Language { get; init; }
}

public sealed class Rect {
  public double X { get; init; }
  public double Y { get; init; }
  public double Width { get; init; }
  public double Height { get; init; }
}
