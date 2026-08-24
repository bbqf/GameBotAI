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
}
