using System.Text.RegularExpressions;
using GameBot.Domain.Parameters;
using GameBot.Domain.QueueTemplates;

namespace GameBot.Domain.QueueSettings;

/// <summary>
/// The shared rules for a queue template. The queue template route and the gamepack validator (feature 150) use the same
/// checks, so the rules cannot drift apart.
/// </summary>
public static class QueueTemplateRules {
  /// <summary>The largest length of a template name.</summary>
  public const int MaxNameLength = 100;

  private static readonly Regex NamePattern =
    new("^[A-Za-z0-9 _-]+$", RegexOptions.Compiled, TimeSpan.FromMilliseconds(500));

  /// <summary>Checks a trimmed template name. Gives the error text, or null when the name is valid.</summary>
  /// <param name="trimmedName">The name, trimmed.</param>
  public static string? ValidateName(string? trimmedName) {
    if (string.IsNullOrWhiteSpace(trimmedName)) {
      return "name is required";
    }

    if (trimmedName.Length > MaxNameLength) {
      return "name must be 100 characters or fewer";
    }

    if (!NamePattern.IsMatch(trimmedName)) {
      return "name may contain only letters, digits, spaces, hyphens, and underscores";
    }

    return null;
  }

  /// <summary>
  /// Checks the timer fields of one entry. A <see cref="ScheduleType.Timer"/> entry needs exactly one of the two fields.
  /// Gives the error text, or null.
  /// </summary>
  /// <param name="scheduleType">The schedule type.</param>
  /// <param name="hasTimeOfDay">True when the time of day is set.</param>
  /// <param name="hasRelativeOffset">True when the relative offset is set.</param>
  public static string? ValidateTimerFields(ScheduleType scheduleType, bool hasTimeOfDay, bool hasRelativeOffset) =>
    scheduleType == ScheduleType.Timer && hasTimeOfDay == hasRelativeOffset
      ? "must set exactly one of timerTimeOfDay or timerRelativeOffset when scheduleType is Timer"
      : null;

  /// <summary>Checks all parameter value names of one entry. Gives one error text for each bad name.</summary>
  /// <param name="values">The parameter values.</param>
  public static IReadOnlyList<(string Name, string Error)> ValidateParameterNames(IEnumerable<ParameterBinding> values) {
    ArgumentNullException.ThrowIfNull(values);
    var errors = new List<(string, string)>();
    foreach (var binding in values) {
      var error = ParameterNameRules.ValidateName(binding.Name);
      if (error is not null) {
        errors.Add((binding.Name, error));
      }
    }

    return errors;
  }

  /// <summary>
  /// Checks a template of the settings document shape (a gamepack seed). Gives one error text for each problem.
  /// It does not check that the template name is unique. The caller does that.
  /// </summary>
  /// <param name="template">The template.</param>
  public static IReadOnlyList<string> ValidateDoc(QueueTemplateDoc template) {
    ArgumentNullException.ThrowIfNull(template);
    var errors = new List<string>();
    var nameError = ValidateName(template.Name?.Trim());
    if (nameError is not null) {
      errors.Add("The template " + nameError + ".");
    }

    for (var i = 0; i < template.Entries.Count; i++) {
      var entry = template.Entries[i];
      if (entry is null) {
        errors.Add($"Entry {i} is empty.");
        continue;
      }

      if (string.IsNullOrWhiteSpace(entry.Sequence)) {
        errors.Add($"Entry {i} has no sequence name.");
      }

      if (!Enum.IsDefined(entry.ScheduleType)) {
        errors.Add($"Entry {i} has a schedule type that is not valid.");
        continue;
      }

      var timerError = ValidateTimerFields(entry.ScheduleType, entry.TimerTimeOfDay.HasValue, entry.TimerRelativeOffset.HasValue);
      if (timerError is not null) {
        errors.Add($"Entry {i} {timerError}.");
      }
      else if (entry.ScheduleType != ScheduleType.Timer && (entry.TimerTimeOfDay.HasValue || entry.TimerRelativeOffset.HasValue)) {
        errors.Add($"Entry {i} sets a timer field, but scheduleType is not Timer.");
      }

      foreach (var (name, error) in ValidateParameterNames(entry.ParameterValues ?? Array.Empty<ParameterBinding>())) {
        errors.Add($"Entry {i}: parameter value '{name}': {error}.");
      }
    }

    return errors;
  }
}
