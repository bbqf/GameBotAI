namespace GameBot.Domain.Commands;

/// <summary>The result of <see cref="TextInputRules.Check"/> (feature 192).</summary>
public enum TextInputCheck {
  /// <summary>The text is valid.</summary>
  Ok,

  /// <summary>The text is null or empty.</summary>
  Empty,

  /// <summary>The text has more than <see cref="TextInputRules.MaxLength"/> characters.</summary>
  TooLong,

  /// <summary>The text has a character outside U+0020 to U+007E.</summary>
  UnsupportedCharacter
}

/// <summary>
/// The rules for the text of a text input step (feature 192): the permitted characters and the
/// maximum length.
/// </summary>
public static class TextInputRules {
  /// <summary>The maximum number of characters in a text.</summary>
  public const int MaxLength = 500;

  /// <summary>The first permitted character (space).</summary>
  public const char MinCharacter = ' ';

  /// <summary>The last permitted character (<c>~</c>).</summary>
  public const char MaxCharacter = '~';

  /// <summary>Reason code for an empty text.</summary>
  public const string EmptyTextReason = "text_input_empty_text";

  /// <summary>Reason code for a text that is too long.</summary>
  public const string TooLongReason = "text_input_too_long";

  /// <summary>Reason code for a text with a character that is not permitted.</summary>
  public const string UnsupportedCharacterReason = "text_input_unsupported_character";

  /// <summary>Reason code for a device that does not accept the input.</summary>
  public const string NotAcceptedReason = "text_input_not_accepted";

  /// <summary>Reason code for a step without a <c>textInput</c> config.</summary>
  public const string MissingConfigReason = "text_input_missing_config";

  /// <summary>
  /// Checks <paramref name="text"/>. The check order is: empty, then length, then characters.
  /// </summary>
  /// <param name="text">The text to check.</param>
  public static TextInputCheck Check(string? text) {
    if (string.IsNullOrEmpty(text)) return TextInputCheck.Empty;
    if (text.Length > MaxLength) return TextInputCheck.TooLong;
    foreach (var c in text) {
      if (c < MinCharacter || c > MaxCharacter) return TextInputCheck.UnsupportedCharacter;
    }

    return TextInputCheck.Ok;
  }

  /// <summary>Gives the reason code of a failed check, or null for <see cref="TextInputCheck.Ok"/>.</summary>
  /// <param name="check">The check result.</param>
  public static string? ReasonCode(TextInputCheck check) => check switch {
    TextInputCheck.Empty => EmptyTextReason,
    TextInputCheck.TooLong => TooLongReason,
    TextInputCheck.UnsupportedCharacter => UnsupportedCharacterReason,
    _ => null
  };
}
