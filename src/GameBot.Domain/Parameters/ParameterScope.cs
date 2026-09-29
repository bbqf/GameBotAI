using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Linq;
using GameBot.Domain.Queues;
using GameBot.Domain.Utils;

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
/// <param name="Sources">
/// Set only for a binding value with text around one or more placeholders. One item for each
/// placeholder name, with its value and origin layer. For all other values, this is <c>null</c>.
/// </param>
public readonly record struct ParameterValue(
    string Text,
    string OriginLayer,
    IReadOnlyList<ResolvedParameter>? Sources = null);

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
  private readonly Dictionary<string, ParameterValue> _values;
  private readonly Dictionary<string, ParameterDeclaration> _declarations;

  private ParameterScope(
      ParameterScope? parent,
      string layerName,
      Dictionary<string, ParameterValue> values,
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
      new Dictionary<string, ParameterValue>(StringComparer.Ordinal),
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

    const string layer = ParameterScopeLayers.Queue;
    var values = new Dictionary<string, ParameterValue>(StringComparer.Ordinal);
    if (!string.IsNullOrWhiteSpace(queue.EmulatorSerial))
      values[ParameterNameRules.QueueEmulatorSerial] = new ParameterValue(queue.EmulatorSerial, layer);
    if (!string.IsNullOrWhiteSpace(queue.EmulatorInstanceName))
      values[ParameterNameRules.QueueInstanceName] = new ParameterValue(queue.EmulatorInstanceName!, layer);
    if (queue.EmulatorInstanceIndex is { } index)
      values[ParameterNameRules.QueueInstanceIndex] = new ParameterValue(index.ToString(CultureInfo.InvariantCulture), layer);
    if (!string.IsNullOrWhiteSpace(queue.LinkedGameId))
      values[ParameterNameRules.QueueGameId] = new ParameterValue(queue.LinkedGameId!, layer);

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
    var values = new Dictionary<string, ParameterValue>(StringComparer.Ordinal);
    if (bindings is not null) {
      foreach (var binding in bindings) {
        if (binding?.Name is null || binding.Value is null) continue;
        values[binding.Name] = new ParameterValue(binding.Value, layerName);
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
  /// Returns a new inner layer for the <paramref name="bindings"/> of a step that runs a command
  /// (feature 115). A <c>{{name}}</c> placeholder in a binding value resolves against this scope
  /// (the scope outside the new layer), never against the new layer. The receiver does not change.
  /// <list type="bullet">
  /// <item>A value with no placeholder stays literal, with the layer <paramref name="layerName"/>.
  /// <c>${name}</c> is not a placeholder.</item>
  /// <item>A value that is exactly one placeholder gets the resolved value and keeps its origin layer.</item>
  /// <item>A value with text around placeholders gets each placeholder replaced, the layer
  /// <paramref name="layerName"/>, and one source item for each placeholder name.</item>
  /// <item>A binding with a <c>null</c> value is skipped, so the name inherits the outer value.</item>
  /// </list>
  /// </summary>
  /// <param name="layerName">One of <see cref="ParameterScopeLayers"/>, usually <see cref="ParameterScopeLayers.Command"/>.</param>
  /// <param name="bindings">The bindings of the step.</param>
  /// <param name="child">The new layer, when all placeholders resolved.</param>
  /// <param name="error">
  /// When a placeholder does not resolve: the error with the field path
  /// <c>parameterBindings.&lt;bindingName&gt;</c> and the reason <see cref="ParameterResolutionReasons.Unresolved"/>.
  /// </param>
  /// <returns><c>true</c> when all placeholders resolved; otherwise <c>false</c>.</returns>
  public bool TryBindChild(
      string layerName,
      IEnumerable<ParameterBinding>? bindings,
      [NotNullWhen(true)] out ParameterScope? child,
      [NotNullWhen(false)] out ParameterResolutionError? error) {
    var values = new Dictionary<string, ParameterValue>(StringComparer.Ordinal);
    IReadOnlyDictionary<string, string>? context = null;
    foreach (var binding in bindings ?? Array.Empty<ParameterBinding>()) {
      if (binding?.Name is null || binding.Value is null) continue;
      var text = binding.Value;
      var keys = TemplateSubstitutor.ExtractKeys(text);
      string? missing = null;
      if (keys.Count == 0) {
        values[binding.Name] = new ParameterValue(text, layerName);
      }
      else if (keys.Count == 1 && string.Equals(text, $"{{{{{keys[0]}}}}}", StringComparison.Ordinal)) {
        if (TryResolve(keys[0], out var whole)) values[binding.Name] = whole;
        else missing = keys[0];
      }
      else {
        context ??= ToSubstitutionContext();
        if (TemplateSubstitutor.TrySubstitute(text, context, out var composed, out var unresolved)) {
          var sources = new List<ResolvedParameter>(keys.Count);
          foreach (var key in keys) {
            TryResolve(key, out var source);
            sources.Add(new ResolvedParameter(key, source.Text, source.OriginLayer));
          }
          values[binding.Name] = new ParameterValue(composed, layerName, sources);
        }
        else missing = unresolved[0];
      }

      if (missing is not null) {
        child = null;
        error = new ParameterResolutionError(missing, $"parameterBindings.{binding.Name}", ParameterResolutionReasons.Unresolved);
        return false;
      }
    }

    child = new ParameterScope(this, layerName, values, new Dictionary<string, ParameterDeclaration>(StringComparer.Ordinal));
    error = null;
    return true;
  }

  /// <summary>
  /// Returns a new innermost layer carrying the loop iteration value, so a body step resolves both
  /// <c>{{iteration}}</c> and its parameters in one pass.
  /// </summary>
  /// <param name="iteration">The current 1-based iteration.</param>
  public ParameterScope WithIteration(int iteration) {
    var values = new Dictionary<string, ParameterValue>(StringComparer.Ordinal) {
      [ParameterNameRules.IterationName] = new ParameterValue(
          iteration.ToString(CultureInfo.InvariantCulture), ParameterScopeLayers.Loop)
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
        // A whole-placeholder binding keeps the layer that supplied its value (feature 115).
        value = bound;
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
