namespace GameBot.Domain.Parameters;

/// <summary>
/// A value supplied for a parameter at one call site — a queue-template entry binding a sequence's
/// parameters, or a sequence's command step binding a command's parameters (feature 078).
/// </summary>
public sealed class ParameterBinding {
  /// <summary>
  /// The parameter name this binding supplies. A name that matches a declaration on the callee is a
  /// <em>declared</em> binding; on a queue-template entry a name that matches nothing is an
  /// <em>ad-hoc</em> value, which still enters the run scope and can be inherited at any depth.
  /// </summary>
  public required string Name { get; init; }

  /// <summary>
  /// The supplied value, or <c>null</c> to inherit.
  /// <para>
  /// The distinction is load-bearing and must not be collapsed: <c>null</c> means "this call site
  /// supplies nothing, keep resolving outward", whereas the empty string is a real value that
  /// satisfies resolution and stops the walk. Binding forms default every row to <c>null</c> so the
  /// common inherit-everything case needs no interaction.
  /// </para>
  /// </summary>
  public string? Value { get; init; }
}
