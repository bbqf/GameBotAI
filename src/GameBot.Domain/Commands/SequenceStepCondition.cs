using System.Text.Json.Serialization;

namespace GameBot.Domain.Commands;

[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(ImageVisibleStepCondition), typeDiscriminator: "imageVisible")]
[JsonDerivedType(typeof(CommandOutcomeStepCondition), typeDiscriminator: "commandOutcome")]
public abstract class SequenceStepCondition {
  public abstract string Type { get; }
}

public sealed class ImageVisibleStepCondition : SequenceStepCondition {
  public override string Type => "imageVisible";
  public string ImageId { get; set; } = string.Empty;
  public double? MinSimilarity { get; set; }
}

public sealed class CommandOutcomeStepCondition : SequenceStepCondition {
  public override string Type => "commandOutcome";
  public string StepRef { get; set; } = string.Empty;
  public string ExpectedState { get; set; } = string.Empty;
}
