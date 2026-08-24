using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace GameBot.Domain.Parameters;

/// <summary>
/// The single definition of what a parameter name may be (feature 078), shared by the domain, the
/// API validators and the scope builder so the three cannot drift apart.
/// </summary>
public static class ParameterNameRules {
  /// <summary>Reserved namespace prefix under which queue-derived built-in values are exposed.</summary>
  public const string BuiltInNamespace = "queue";

  /// <summary>Reserved name carrying the current 1-based loop iteration; valid only inside a loop.</summary>
  public const string IterationName = "iteration";

  /// <summary>Built-in exposing the executing queue's ADB device serial.</summary>
  public const string QueueEmulatorSerial = "queue.emulatorSerial";

  /// <summary>Built-in exposing the executing queue's LDPlayer instance name.</summary>
  public const string QueueInstanceName = "queue.instanceName";

  /// <summary>Built-in exposing the executing queue's LDPlayer instance index.</summary>
  public const string QueueInstanceIndex = "queue.instanceIndex";

  /// <summary>Built-in exposing the executing queue's linked game id.</summary>
  public const string QueueGameId = "queue.gameId";

  private static readonly Regex IdentifierPattern =
      new(@"^[A-Za-z_]\w*$", RegexOptions.Compiled, TimeSpan.FromMilliseconds(500));

  /// <summary>
  /// Every built-in name, paired with its declared type and operator-facing description. Ordered for
  /// stable display in the insert-parameter picker.
  /// </summary>
  public static IReadOnlyList<ParameterDeclaration> BuiltIns { get; } = new[] {
    new ParameterDeclaration {
      Name = QueueEmulatorSerial, Type = ParameterValueType.Text,
      Description = "The executing queue's bound ADB device serial."
    },
    new ParameterDeclaration {
      Name = QueueInstanceName, Type = ParameterValueType.Text,
      Description = "The executing queue's LDPlayer instance name, when one is configured."
    },
    new ParameterDeclaration {
      Name = QueueInstanceIndex, Type = ParameterValueType.Number,
      Description = "The executing queue's LDPlayer instance index, when one is configured."
    },
    new ParameterDeclaration {
      Name = QueueGameId, Type = ParameterValueType.Text,
      Description = "The game linked to the executing queue, when one is linked."
    }
  };

  /// <summary>True when <paramref name="name"/> is one of the reserved queue built-ins.</summary>
  /// <param name="name">Candidate name.</param>
  public static bool IsBuiltIn(string name) =>
      BuiltIns.Any(b => string.Equals(b.Name, name, System.StringComparison.Ordinal));

  /// <summary>
  /// True when <paramref name="name"/> uses the reserved built-in namespace, whether or not it names
  /// an actual built-in. Used to reject <c>queue.anything</c> as a user declaration.
  /// </summary>
  /// <param name="name">Candidate name.</param>
  public static bool UsesBuiltInNamespace(string name) =>
      name is not null && name.StartsWith(BuiltInNamespace + ".", System.StringComparison.Ordinal);

  /// <summary>
  /// Validates a name an operator may declare or bind ad hoc. Returns <c>null</c> when acceptable,
  /// otherwise the operator-facing reason it is not.
  /// </summary>
  /// <param name="name">Candidate name.</param>
  public static string? ValidateName(string? name) {
    if (string.IsNullOrWhiteSpace(name)) return "parameter name must not be empty";
    if (string.Equals(name, IterationName, System.StringComparison.Ordinal))
      return $"parameter name '{IterationName}' is reserved for the loop iteration value";
    if (UsesBuiltInNamespace(name) || string.Equals(name, BuiltInNamespace, System.StringComparison.Ordinal))
      return $"parameter names may not use the reserved '{BuiltInNamespace}' namespace";
    if (!IdentifierPattern.IsMatch(name))
      return $"parameter name '{name}' is not a valid identifier (letters, digits and underscore; must not start with a digit)";
    return null;
  }

  /// <summary>
  /// Validates a whole declaration list for one entity: name rules, duplicates (rejected even when
  /// they differ only by case, so two names can never resolve to one value), and numeric defaults.
  /// </summary>
  /// <param name="declarations">Declarations to check; <c>null</c> is treated as empty.</param>
  /// <returns>Operator-facing error strings; empty when the list is valid.</returns>
  public static IReadOnlyList<string> ValidateDeclarations(IEnumerable<ParameterDeclaration>? declarations) {
    var errors = new List<string>();
    if (declarations is null) return errors;

    var seen = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
    foreach (var declaration in declarations) {
      var nameError = ValidateName(declaration?.Name);
      if (nameError is not null) {
        errors.Add(nameError);
        continue;
      }

      if (!seen.Add(declaration!.Name)) {
        errors.Add($"duplicate parameter name '{declaration.Name}'");
        continue;
      }

      if (declaration.Type == ParameterValueType.Number
          && declaration.Default is not null
          && !int.TryParse(declaration.Default, NumberStyles.Integer, CultureInfo.InvariantCulture, out _)) {
        errors.Add($"default value '{declaration.Default}' for numeric parameter '{declaration.Name}' is not a whole number");
      }
    }

    return errors;
  }
}
