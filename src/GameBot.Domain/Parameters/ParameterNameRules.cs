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

      AddFriendlyErrors(declaration, errors);
    }

    return errors;
  }

  /// <summary>Marker in a number pattern that stands for the entered number (feature 168).</summary>
  public const string PatternMarker = "{value}";

  private const int MaxLabelLength = 80;
  private const int MaxHelpTextLength = 300;
  private const int MaxUnitLength = 20;
  private const int MaxListItems = 50;

  /// <summary>
  /// True when <paramref name="value"/> is exactly one <c>{{name}}</c> placeholder with no other text.
  /// Such an option value would collide with a pass-through binding (feature 168, FR-027).
  /// </summary>
  /// <param name="value">Candidate option value.</param>
  public static bool IsWholePlaceholder(string? value) {
    if (value is null || value.Length < 5) return false;
    if (!value.StartsWith("{{", System.StringComparison.Ordinal)
        || !value.EndsWith("}}", System.StringComparison.Ordinal)) return false;
    var inner = value.Substring(2, value.Length - 4);
    return inner.Length > 0 && !inner.Contains("{{", System.StringComparison.Ordinal)
        && !inner.Contains("}}", System.StringComparison.Ordinal);
  }

  private static void AddFriendlyErrors(ParameterDeclaration d, List<string> errors) {
    var name = d.Name;
    var kind = d.Kind;

    if (kind is not null && !System.Enum.IsDefined(kind.Value)) {
      errors.Add($"parameter '{name}' has an unknown kind. Use text, number, choice, or image.");
      return;
    }

    var effective = d.EffectiveKind;
    var isNumberType = d.Type == ParameterValueType.Number;

    if (kind == ParameterInputKind.Text && isNumberType)
      errors.Add($"parameter '{name}' has kind text but type number. Set the kind to number or choice, or set the type to text.");
    if (kind == ParameterInputKind.Number && !isNumberType)
      errors.Add($"parameter '{name}' has kind number but type text. Set the type to number, or use another kind.");
    if (kind == ParameterInputKind.Image && isNumberType)
      errors.Add($"parameter '{name}' has kind image but type number. Set the type to text, or use another kind.");

    if (d.Label is not null && (string.IsNullOrWhiteSpace(d.Label) || d.Label.Length > MaxLabelLength))
      errors.Add($"label of parameter '{name}' must have 1 to {MaxLabelLength} characters. Shorten the label or remove it.");
    if (d.HelpText is not null && d.HelpText.Length > MaxHelpTextLength)
      errors.Add($"help text of parameter '{name}' has more than {MaxHelpTextLength} characters. Shorten the help text.");

    var hasOptions = d.Options is not null;
    if (effective != ParameterInputKind.Choice && hasOptions)
      errors.Add($"parameter '{name}' has options but is not a choice. Remove the options or set the kind to choice.");
    if (effective != ParameterInputKind.Image && d.AllowedImages is not null)
      errors.Add($"parameter '{name}' has allowed images but is not an image. Remove the images or set the kind to image.");
    if (effective != ParameterInputKind.Number && (d.Min is not null || d.Max is not null || d.Unit is not null))
      errors.Add($"parameter '{name}' has a range or unit but is not a number. Remove them or set the kind to number.");
    if (!isNumberType && d.Pattern is not null)
      errors.Add($"parameter '{name}' has a pattern but is not a number. Remove the pattern or set the type to number.");

    if (effective == ParameterInputKind.Choice) AddChoiceErrors(d, errors);
    if (effective == ParameterInputKind.Image) AddImageErrors(d, errors);

    if (d.Min is not null && d.Max is not null && d.Min > d.Max)
      errors.Add($"minimum {d.Min} of parameter '{name}' is more than the maximum {d.Max}. Change one of the two numbers.");
    if (d.Unit is not null && d.Unit.Length > MaxUnitLength)
      errors.Add($"unit of parameter '{name}' has more than {MaxUnitLength} characters. Shorten the unit.");
    if (d.Pattern is not null && isNumberType && CountMarkers(d.Pattern) != 1)
      errors.Add($"pattern of parameter '{name}' must have exactly one {PatternMarker} marker. Write the marker one time in the pattern.");

    AddDefaultFitError(d, errors);
  }

  private static int CountMarkers(string pattern) {
    var count = 0;
    var index = 0;
    while ((index = pattern.IndexOf(PatternMarker, index, System.StringComparison.Ordinal)) >= 0) {
      count++;
      index += PatternMarker.Length;
    }

    return count;
  }

  private static void AddChoiceErrors(ParameterDeclaration d, List<string> errors) {
    var name = d.Name;
    var options = d.Options;
    if (options is null || options.Count == 0) {
      errors.Add($"choice parameter '{name}' needs at least one option. Add an option with a label and a value.");
      return;
    }

    if (options.Count > MaxListItems) {
      errors.Add($"choice parameter '{name}' has {options.Count} options. Keep {MaxListItems} options or fewer.");
      return;
    }

    var labels = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
    var values = new HashSet<string>(System.StringComparer.Ordinal);
    foreach (var option in options) {
      if (string.IsNullOrWhiteSpace(option.Label) || option.Label.Length > MaxLabelLength) {
        errors.Add($"an option of parameter '{name}' has no label or a label longer than {MaxLabelLength} characters. Write a label of 1 to {MaxLabelLength} characters.");
        continue;
      }

      if (string.IsNullOrEmpty(option.Value)) {
        errors.Add($"option '{option.Label}' of parameter '{name}' has no value. Write the technical value for the option.");
        continue;
      }

      if (!labels.Add(option.Label))
        errors.Add($"option label '{option.Label}' of parameter '{name}' is used two times. Give each option a different label.");
      if (!values.Add(option.Value))
        errors.Add($"option value '{option.Value}' of parameter '{name}' is used two times. Give each option a different value.");
      if (d.Type == ParameterValueType.Number
          && !int.TryParse(option.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
        errors.Add($"option value '{option.Value}' of numeric parameter '{name}' is not a whole number. Write a whole number.");
      if (IsWholePlaceholder(option.Value))
        errors.Add($"option value '{option.Value}' of parameter '{name}' is only a placeholder. Add text around the placeholder, or use a parameter of the sequence at the call site.");
    }
  }

  private static void AddImageErrors(ParameterDeclaration d, List<string> errors) {
    var name = d.Name;
    var images = d.AllowedImages;
    if (images is null || images.Count == 0) {
      errors.Add($"image parameter '{name}' needs at least one allowed image. Choose one or more images.");
      return;
    }

    if (images.Count > MaxListItems) {
      errors.Add($"image parameter '{name}' has {images.Count} allowed images. Keep {MaxListItems} images or fewer.");
      return;
    }

    if (images.Any(string.IsNullOrWhiteSpace))
      errors.Add($"image parameter '{name}' has an empty image ID. Remove the empty entry.");
    if (images.Distinct(System.StringComparer.Ordinal).Count() != images.Count)
      errors.Add($"image parameter '{name}' lists an image two times. Remove the duplicate.");
  }

  private static void AddDefaultFitError(ParameterDeclaration d, List<string> errors) {
    if (d.Default is null) return;
    var name = d.Name;
    switch (d.EffectiveKind) {
      case ParameterInputKind.Choice when d.Options is not null && d.Options.All(o => o.Value != d.Default):
        errors.Add($"default value '{d.Default}' of choice parameter '{name}' is not one of the options. Pick an option value as the default.");
        break;
      case ParameterInputKind.Image when d.AllowedImages is not null && !d.AllowedImages.Contains(d.Default):
        errors.Add($"default value '{d.Default}' of image parameter '{name}' is not an allowed image. Pick an allowed image as the default.");
        break;
      case ParameterInputKind.Number
          when int.TryParse(d.Default, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number)
          && ((d.Min is not null && number < d.Min) || (d.Max is not null && number > d.Max)):
        errors.Add($"default value '{d.Default}' of numeric parameter '{name}' is outside the range. Pick a number between the minimum and the maximum.");
        break;
    }
  }
}
