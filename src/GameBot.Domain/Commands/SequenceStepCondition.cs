using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace GameBot.Domain.Commands;

[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(ImageVisibleStepCondition), typeDiscriminator: "imageVisible")]
[JsonDerivedType(typeof(CommandOutcomeStepCondition), typeDiscriminator: "commandOutcome")]
[JsonDerivedType(typeof(AllStepCondition), typeDiscriminator: "all")]
[JsonDerivedType(typeof(AnyStepCondition), typeDiscriminator: "any")]
[JsonDerivedType(typeof(NoneStepCondition), typeDiscriminator: "none")]
[JsonDerivedType(typeof(LastRunStepCondition), typeDiscriminator: "lastRun")]
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

/// <summary>
/// True when the named sequence has a completed run with the given status in the current queue, and
/// the end time of that run is in the window (feature 105). The window starts at the last occurrence
/// of a local time of day (<see cref="Since"/>) or at now minus a duration (<see cref="Within"/>), and
/// ends now. In a run that has no queue, the condition is false.
/// <para>
/// The text fields stay as the author wrote them, so a read returns the same text.
/// <see cref="LastRunConditionRules"/> holds the field rules and the parsers.
/// </para>
/// </summary>
public sealed class LastRunStepCondition : SequenceStepCondition {
  /// <summary>The value of <see cref="Sequence"/> that names the sequence that contains the step.</summary>
  public const string SelfSequence = "self";

  [JsonIgnore]
  public override string Type => "lastRun";

  /// <summary><c>self</c>, or a sequence ID.</summary>
  public string Sequence { get; set; } = string.Empty;

  /// <summary><c>success</c>, <c>failure</c> or <c>cancelled</c> (not case-sensitive).</summary>
  public string Status { get; set; } = string.Empty;

  /// <summary>A local time of day in <c>HH:mm</c> format. Set exactly one of this field and <see cref="Within"/>.</summary>
  public string? Since { get; set; }

  /// <summary>A duration in <c>hh:mm:ss</c> or <c>d.hh:mm:ss</c> format. Set exactly one of this field and <see cref="Since"/>.</summary>
  public string? Within { get; set; }
}

/// <summary>
/// How a <see cref="CompositeStepCondition"/> combines the results of its children (feature 088).
/// </summary>
public enum CompositeConditionRule {
  /// <summary>True only when every child is true.</summary>
  All,

  /// <summary>True when at least one child is true.</summary>
  Any,

  /// <summary>True only when no child is true.</summary>
  None
}

/// <summary>
/// A condition that combines other conditions (feature 088, issue #191).
/// <para>
/// A single reference image is not always enough to identify a screen: two unrelated dialogs can
/// draw an identical button at identical coordinates, so one template matches both at full
/// confidence and no threshold change can separate them. A composite lets a guard require a second,
/// dialog-unique signal alongside the ambiguous one — "this button AND that title", or "this button
/// AND NOT that other dialog's title".
/// </para>
/// <para>
/// The combining rule is carried by the JSON <c>type</c> discriminator rather than by a field, so
/// each rule is its own sealed subclass. That keeps an unknown or missing rule unrepresentable and
/// gives <see cref="System.Text.Json"/> the one-type-per-discriminator mapping it needs in order to
/// serialize.
/// </para>
/// </summary>
public abstract class CompositeStepCondition : SequenceStepCondition {
  /// <summary>The rule this composite applies to <see cref="Children"/>.</summary>
  [JsonIgnore]
  public abstract CompositeConditionRule Rule { get; }

  /// <summary>
  /// The child conditions, in author order, which is also evaluation order. Between 1 and
  /// <see cref="MaxChildren"/> entries; an empty list is rejected at save time rather than being
  /// given an arbitrary truth value at run time. A child may itself be a composite, up to
  /// <see cref="MaxDepth"/> levels.
  /// </summary>
  public IReadOnlyList<SequenceStepCondition> Children { get; init; } = Array.Empty<SequenceStepCondition>();

  /// <summary>
  /// Maximum number of children in one composite. A safety bound, not a design constraint: real
  /// guards combine two or three signals, and every child may cost a screen evaluation.
  /// </summary>
  public const int MaxChildren = 16;

  /// <summary>
  /// Maximum condition nesting depth, counting condition levels rather than JSON object levels: a
  /// leaf alone is depth 1, a composite of leaves is depth 2. Bounds both validation recursion and
  /// evaluation cost against a hostile or accidentally recursive payload.
  /// </summary>
  public const int MaxDepth = 4;
}

/// <summary>True only when every child condition is true. Stops at the first false child.</summary>
public sealed class AllStepCondition : CompositeStepCondition {
  [JsonIgnore]
  public override string Type => "all";

  [JsonIgnore]
  public override CompositeConditionRule Rule => CompositeConditionRule.All;
}

/// <summary>True when at least one child condition is true. Stops at the first true child.</summary>
public sealed class AnyStepCondition : CompositeStepCondition {
  [JsonIgnore]
  public override string Type => "any";

  [JsonIgnore]
  public override CompositeConditionRule Rule => CompositeConditionRule.Any;
}

/// <summary>
/// True only when no child condition is true. Stops at the first true child. Equivalent to a negated
/// <see cref="AnyStepCondition"/>, kept as its own rule because "and not that dialog" is the idiom
/// this feature exists to express, and spelling it directly reads better than composing a negation.
/// </summary>
public sealed class NoneStepCondition : CompositeStepCondition {
  [JsonIgnore]
  public override string Type => "none";

  [JsonIgnore]
  public override CompositeConditionRule Rule => CompositeConditionRule.None;
}
