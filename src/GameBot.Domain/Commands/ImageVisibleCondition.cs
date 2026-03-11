namespace GameBot.Domain.Commands;

public sealed class ImageVisibleCondition {
  public string Type { get; set; } = "imageVisible";
  public string ImageId { get; set; } = string.Empty;
  public double? MinSimilarity { get; set; }
}
