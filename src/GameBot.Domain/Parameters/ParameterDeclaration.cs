using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace GameBot.Domain.Parameters;

/// <summary>
/// A named, typed input that a command or a sequence accepts (feature 078).
/// <para>
/// Declarations are what make a parameter discoverable: the authoring UI renders them as a binding
/// form on every call site, and validation uses them to decide whether a <c>{{name}}</c> reference
/// can be satisfied. An entity with no declarations behaves exactly as it did before this feature.
/// </para>
/// </summary>
public sealed class ParameterDeclaration {
  /// <summary>
  /// Unique name within the owning entity, matched case-sensitively when referenced. Must be a valid
  /// identifier, must not be the reserved loop name <c>iteration</c>, and must not use the reserved
  /// <c>queue</c> namespace. See <see cref="ParameterNameRules"/>.
  /// </summary>
  public required string Name { get; init; }

  /// <summary>The declared value type. Defaults to <see cref="ParameterValueType.Text"/>.</summary>
  public ParameterValueType Type { get; init; } = ParameterValueType.Text;

  /// <summary>
  /// Value used when no call site and no enclosing scope supplies one. <c>null</c> means the
  /// parameter has no default, so an unsupplied reference fails the step. When
  /// <see cref="Type"/> is <see cref="ParameterValueType.Number"/> this must parse as a whole number.
  /// </summary>
  public string? Default { get; init; }

  /// <summary>
  /// When <c>true</c>, a queue whose enabled entries cannot supply this parameter is refused at
  /// start rather than failing mid-run. A declaration may be both required and defaulted, in which
  /// case the default always satisfies it.
  /// </summary>
  public bool Required { get; init; }

  /// <summary>Operator-facing explanation, shown in the insert-parameter picker and binding forms.</summary>
  public string? Description { get; init; }

  /// <summary>Readable name for the person who fills the value (feature 168). 1 to 80 characters when set.</summary>
  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public string? Label { get; init; }

  /// <summary>Short help text under the field (feature 168). Up to 300 characters.</summary>
  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public string? HelpText { get; init; }

  /// <summary>
  /// How the value is entered. <c>null</c> means: derive from <see cref="Type"/> (feature 168).
  /// </summary>
  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public ParameterInputKind? Kind { get; init; }

  /// <summary>The options of a <see cref="ParameterInputKind.Choice"/> parameter. 1 to 50 items.</summary>
  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public IReadOnlyList<ParameterOption>? Options { get; init; }

  /// <summary>The IDs of the images that a person may select for an image parameter. 1 to 50 items.</summary>
  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public IReadOnlyList<string>? AllowedImages { get; init; }

  /// <summary>Lowest allowed whole number for a number parameter.</summary>
  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public int? Min { get; init; }

  /// <summary>Highest allowed whole number for a number parameter.</summary>
  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public int? Max { get; init; }

  /// <summary>Unit text that the form shows next to a number, for example "ms". Up to 20 characters.</summary>
  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public string? Unit { get; init; }

  /// <summary>
  /// Technical text with exactly one <c>{value}</c> marker. The entered number goes into the marker
  /// when the name is resolved. The result is text.
  /// </summary>
  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public string? Pattern { get; init; }

  /// <summary>The kind in use: <see cref="Kind"/> when set, otherwise the kind that follows from <see cref="Type"/>.</summary>
  [JsonIgnore]
  public ParameterInputKind EffectiveKind =>
      Kind ?? (Type == ParameterValueType.Number ? ParameterInputKind.Number : ParameterInputKind.Text);
}
