using System.Text.Json.Serialization;

namespace GameBot.Domain.Commands;

[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(ImageVisibleStepCondition), typeDiscriminator: "imageVisible")]
[JsonDerivedType(typeof(CommandOutcomeStepCondition), typeDiscriminator: "commandOutcome")]
public abstract class SequenceStepCondition {
  public abstract string Type { get; }
  public bool Negate { get; set; }
}

// [JsonIgnore] on each override below: the "type" discriminator (JsonPolymorphic above) already
// serializes this value under the same property name. Serializing both produces a duplicate "type"
// key once PropertyNamingPolicy lowercases "Type" to "type" (e.g. JsonSerializerDefaults.Web),
// which then fails to round-trip with "duplicate 'type' metadata property". JsonIgnore on the
// abstract base member is not honored for overridden properties, so it must be repeated here.

public sealed class ImageVisibleStepCondition : SequenceStepCondition {
  [JsonIgnore]
  public override string Type => "imageVisible";
  public string ImageId { get; set; } = string.Empty;
  public double? MinSimilarity { get; set; }
}

public sealed class CommandOutcomeStepCondition : SequenceStepCondition {
  [JsonIgnore]
  public override string Type => "commandOutcome";
  public string StepRef { get; set; } = string.Empty;
  public string ExpectedState { get; set; } = string.Empty;
}
