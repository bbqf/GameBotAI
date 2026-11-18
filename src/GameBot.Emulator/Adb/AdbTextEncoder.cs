using System.Text;

namespace GameBot.Emulator.Adb;

/// <summary>
/// Prepares a text for <c>adb shell input text</c> (feature 192). The <c>input text</c> tool changes
/// <c>%s</c> to a space, and the device shell splits its arguments at spaces. Thus a space goes to
/// the device as a key event, and no chunk holds <c>%s</c>.
/// </summary>
public static class AdbTextEncoder {
  /// <summary>The Android key code of the space key (<c>KEYCODE_SPACE</c>).</summary>
  public const int SpaceKeyCode = 62;

  /// <summary>
  /// Splits <paramref name="text"/> into parts. Each part is one space (a part that is exactly
  /// <c>" "</c>) or a chunk with no space and no <c>%s</c> sequence. The parts in order give the
  /// original text.
  /// </summary>
  /// <param name="text">The text to split.</param>
  public static IReadOnlyList<string> Split(string text) {
    ArgumentNullException.ThrowIfNull(text);
    var parts = new List<string>();
    var chunk = new StringBuilder();
    foreach (var c in text) {
      if (c == ' ') {
        Flush(parts, chunk);
        parts.Add(" ");
        continue;
      }

      // Split between '%' and 's', so that no chunk holds "%s".
      if (c == 's' && chunk.Length > 0 && chunk[^1] == '%') Flush(parts, chunk);
      chunk.Append(c);
    }

    Flush(parts, chunk);
    return parts;
  }

  /// <summary>True when <paramref name="part"/> is a space part from <see cref="Split"/>.</summary>
  /// <param name="part">A part from <see cref="Split"/>.</param>
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

  private static void Flush(List<string> parts, StringBuilder chunk) {
    if (chunk.Length == 0) return;
    parts.Add(chunk.ToString());
    chunk.Clear();
  }
}
