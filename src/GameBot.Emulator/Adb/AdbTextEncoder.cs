namespace GameBot.Emulator.Adb;

/// <summary>
/// Prepares a text for <c>adb shell input text</c> (feature 192 and 195). The service sends one
/// character in each command, because game input fields drop characters of a burst. A space goes to
/// the device as key event 62, because the device shell splits its arguments at spaces. One
/// character cannot hold the sequence <c>%s</c>, which the <c>input text</c> tool changes to a space.
/// </summary>
public static class AdbTextEncoder {
  /// <summary>The Android key code of the space key (<c>KEYCODE_SPACE</c>).</summary>
  public const int SpaceKeyCode = 62;

  /// <summary>
  /// Splits <paramref name="text"/> into parts of one character, in order (feature 195). A space
  /// part is exactly <c>" "</c>.
  /// </summary>
  /// <param name="text">The text to split.</param>
  public static IReadOnlyList<string> SplitCharacters(string text) {
    ArgumentNullException.ThrowIfNull(text);
    var parts = new List<string>(text.Length);
    foreach (var c in text) {
      parts.Add(new string(c, 1));
    }

    return parts;
  }

  /// <summary>True when <paramref name="part"/> is a space part from <see cref="SplitCharacters"/>.</summary>
  /// <param name="part">A part from <see cref="SplitCharacters"/>.</param>
  public static bool IsSpace(string part) => string.Equals(part, " ", StringComparison.Ordinal);

  /// <summary>
  /// Puts <paramref name="chunk"/> in single quotes for the device shell. Each <c>'</c> becomes
  /// <c>'\''</c>. In single quotes, the device shell gives no special meaning to other characters.
  /// </summary>
  /// <param name="chunk">The chunk to quote.</param>
  public static string QuoteForDeviceShell(string chunk) {
    ArgumentNullException.ThrowIfNull(chunk);
    return "'" + chunk.Replace("'", "'\\''", StringComparison.Ordinal) + "'";
  }
}
