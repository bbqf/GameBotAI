using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using GameBot.Domain.Queues;

namespace GameBot.Domain.Parameters;

/// <summary>Canonical names of the scope layers, used for the UI's effective-value preview.</summary>
public static class ParameterScopeLayers {
  /// <summary>Read-only values derived from the executing queue's own configuration.</summary>
  public const string Queue = "queue";

  /// <summary>Values supplied by the queue-template entry that launched the sequence.</summary>
  public const string Entry = "entry";

  /// <summary>A sequence's own declarations.</summary>
  public const string Sequence = "sequence";

  /// <summary>Bindings supplied by a sequence step invoking a command, plus that command's declarations.</summary>
  public const string Command = "command";

  /// <summary>The ephemeral per-iteration layer carrying <c>iteration</c>.</summary>
  public const string Loop = "loop";

  /// <summary>Reported when a value came from a declaration's default rather than any binding.</summary>
  public const string Default = "default";
}

/// <summary>A resolved parameter value together with the layer that supplied it.</summary>
/// <param name="Text">The resolved value as text; numeric fields parse this at use time.</param>
/// <param name="OriginLayer">One of <see cref="ParameterScopeLayers"/>.</param>
public readonly record struct ParameterValue(string Text, string OriginLayer);

/// <summary>One entry of a scope description, used to render the authoring UI's preview and picker.</summary>
/// <param name="Name">Parameter name.</param>
/// <param name="Value">Effective value, or <c>null</c> when nothing in scope supplies one.</param>
/// <param name="OriginLayer">Layer that supplied the value, or would supply it.</param>
/// <param name="Declared">Whether a declaration by this name exists in the chain.</param>
/// <param name="Description">Operator-facing description from the declaration, when there is one.</param>
public sealed record ScopeEntry(
    string Name,
    string? Value,
    string OriginLayer,
    bool Declared,
    string? Description = null);

/// <summary>
/// The set of parameter names visible to a step at the moment it is dispatched (feature 078).
/// <para>
/// A scope is an <b>immutable</b> node in a chain: <see cref="Child"/> returns a new node and never
/// mutates its parent. That matters because a queue run loop mutates run state on one thread while
/// the queue monitor reads it on another, so a mutable ambient scope would be a race.
/// </para>
/// <para>
/// Resolution walks innermost-first and takes the first match: an explicit binding at the call site
/// beats a value inherited from further out, which beats a declared default. When nothing supplies
/// the name, <see cref="TryResolve"/> returns <c>false</c> and the caller fails the step rather than
/// substituting an empty value.
/// </para>
/// </summary>
public sealed class ParameterScope {
  private readonly Dictionary<string, string> _values;
  private readonly Dictionary<string, ParameterDeclaration> _declarations;

  private ParameterScope(
      ParameterScope? parent,
      string layerName,
      Dictionary<string, string> values,
      Dictionary<string, ParameterDeclaration> declarations) {
    Parent = parent;
    LayerName = layerName;
    _values = values;
    _declarations = declarations;
  }

  /// <summary>An empty scope. Every pre-existing call site defaults to this, preserving behaviour.</summary>
  public static ParameterScope Empty { get; } = new(
      null,
      ParameterScopeLayers.Queue,
      new Dictionary<string, string>(StringComparer.Ordinal),
      new Dictionary<string, ParameterDeclaration>(StringComparer.Ordinal));

  /// <summary>The enclosing scope, or <c>null</c> for the outermost layer.</summary>
  public ParameterScope? Parent { get; }

  /// <summary>Which layer this node represents; one of <see cref="ParameterScopeLayers"/>.</summary>
  public string LayerName { get; }

  /// <summary>
  /// Builds the outermost layer from a queue's own configuration, exposing the four reserved
  /// built-ins. A field that is unset is <b>omitted</b> rather than exposed as empty, so referencing
  /// it behaves exactly like referencing an unknown name.
  /// </summary>
  /// <param name="queue">The queue whose run this scope belongs to; <c>null</c> yields <see cref="Empty"/>.</param>
  public static ParameterScope FromQueue(ExecutionQueue? queue) {
    if (queue is null) return Empty;

    var values = new Dictionary<string, string>(StringComparer.Ordinal);
    if (!string.IsNullOrWhiteSpace(queue.EmulatorSerial))
      values[ParameterNameRules.QueueEmulatorSerial] = queue.EmulatorSerial;
    if (!string.IsNullOrWhiteSpace(queue.EmulatorInstanceName))
      values[ParameterNameRules.QueueInstanceName] = queue.EmulatorInstanceName!;
    if (queue.EmulatorInstanceIndex is { } index)
      values[ParameterNameRules.QueueInstanceIndex] = index.ToString(CultureInfo.InvariantCulture);
    if (!string.IsNullOrWhiteSpace(queue.LinkedGameId))
      values[ParameterNameRules.QueueGameId] = queue.LinkedGameId!;

    var declarations = ParameterNameRules.BuiltIns
        .Where(b => values.ContainsKey(b.Name))
        .ToDictionary(b => b.Name, b => b, StringComparer.Ordinal);

    return new ParameterScope(null, ParameterScopeLayers.Queue, values, declarations);
  }

  /// <summary>
  /// Returns a new inner layer carrying <paramref name="bindings"/> whose value is non-null, plus
  /// <paramref name="declarations"/> whose defaults act as the last resort for this layer's names.
  /// The receiver is unchanged.
  /// </summary>
  /// <param name="layerName">One of <see cref="ParameterScopeLayers"/>.</param>
  /// <param name="bindings">Call-site bindings; entries with a null value mean "inherit" and are skipped.</param>
  /// <param name="declarations">The callee's declarations, supplying defaults and descriptions.</param>
  public ParameterScope Child(
      string layerName,
      IEnumerable<ParameterBinding>? bindings,
      IEnumerable<ParameterDeclaration>? declarations) {
    var values = new Dictionary<string, string>(StringComparer.Ordinal);
    if (bindings is not null) {
      foreach (var binding in bindings) {
        if (binding?.Name is null || binding.Value is null) continue;
        values[binding.Name] = binding.Value;
      }
    }

    var declared = new Dictionary<string, ParameterDeclaration>(StringComparer.Ordinal);
    if (declarations is not null) {
      foreach (var declaration in declarations) {
        if (declaration?.Name is null) continue;
        declared[declaration.Name] = declaration;
      }
    }

    return new ParameterScope(this, layerName, values, declared);
  }

  /// <summary>
  /// Returns a new innermost layer carrying the loop iteration value, so a body step resolves both
  /// <c>{{iteration}}</c> and its parameters in one pass.
  /// </summary>
  /// <param name="iteration">The current 1-based iteration.</param>
  public ParameterScope WithIteration(int iteration) {
    var values = new Dictionary<string, string>(StringComparer.Ordinal) {
      [ParameterNameRules.IterationName] = iteration.ToString(CultureInfo.InvariantCulture)
    };
    return new ParameterScope(
        this,
        ParameterScopeLayers.Loop,
        values,
        new Dictionary<string, ParameterDeclaration>(StringComparer.Ordinal));
  }

  /// <summary>
  /// Resolves <paramref name="name"/> innermost-first: explicit bindings on each layer from here
  /// outward, then the innermost declaration's default.
  /// </summary>
  /// <param name="name">Parameter name, matched case-sensitively.</param>
  /// <param name="value">The resolved value and its originating layer when found.</param>
  /// <returns><c>true</c> when some layer supplied the name; otherwise <c>false</c>.</returns>
  public bool TryResolve(string name, out ParameterValue value) {
    for (var scope = this; scope is not null; scope = scope.Parent) {
      if (scope._values.TryGetValue(name, out var bound)) {
        value = new ParameterValue(bound, scope.LayerName);
        return true;
      }
    }

    for (var scope = this; scope is not null; scope = scope.Parent) {
      if (scope._declarations.TryGetValue(name, out var declaration) && declaration.Default is not null) {
        value = new ParameterValue(declaration.Default, ParameterScopeLayers.Default);
        return true;
      }
    }

    value = default;
    return false;
  }

  /// <summary>
  /// Flattens the chain into a substitution map for <see cref="Utils.TemplateSubstitutor"/>. Inner
  /// layers win, and declared defaults fill names no layer bound.
  /// </summary>
  public IReadOnlyDictionary<string, string> ToSubstitutionContext() {
    var map = new Dictionary<string, string>(StringComparer.Ordinal);
    foreach (var entry in Describe()) {
      if (entry.Value is not null) map[entry.Name] = entry.Value;
    }

    return map;
  }

  /// <summary>
  /// Describes every name visible from here outward, with its effective value and originating layer.
  /// Feeds the authoring UI's effective-value preview and insert-parameter picker.
  /// </summary>
  public IReadOnlyList<ScopeEntry> Describe() {
    var names = new List<string>();
    for (var scope = this; scope is not null; scope = scope.Parent) {
      foreach (var key in scope._values.Keys) {
        if (!names.Contains(key, StringComparer.Ordinal)) names.Add(key);
      }
      foreach (var key in scope._declarations.Keys) {
        if (!names.Contains(key, StringComparer.Ordinal)) names.Add(key);
      }
    }

    var entries = new List<ScopeEntry>(names.Count);
    foreach (var name in names) {
      var found = TryResolve(name, out var resolved);
      entries.Add(new ScopeEntry(
          name,
          found ? resolved.Text : null,
          found ? resolved.OriginLayer : FindDeclaringLayer(name),
          TryFindDeclaration(name, out var declaration),
          declaration?.Description));
    }

    return entries;
  }

  private string FindDeclaringLayer(string name) {
    for (var scope = this; scope is not null; scope = scope.Parent) {
      if (scope._declarations.ContainsKey(name)) return scope.LayerName;
    }

    return LayerName;
  }

  private bool TryFindDeclaration(string name, out ParameterDeclaration? declaration) {
    for (var scope = this; scope is not null; scope = scope.Parent) {
      if (scope._declarations.TryGetValue(name, out var found)) {
        declaration = found;
        return true;
      }
    }

    declaration = null;
    return false;
  }
}
