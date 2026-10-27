using System.Globalization;

namespace GameBot.Emulator.Session;

/// <summary>The result of <see cref="AndroidKeyCodes.TryResolve"/>.</summary>
public enum KeyResolveResult {
  /// <summary>The text maps to a key code.</summary>
  Ok = 0,

  /// <summary>The text is null, empty, or white space.</summary>
  Empty,

  /// <summary>The text is not a listed name and not a whole number.</summary>
  UnknownName,

  /// <summary>The text is a whole number outside 0 to <see cref="AndroidKeyCodes.MaxRawKeyCode"/>.</summary>
  OutOfRange
}

/// <summary>
/// Maps the key text of a key input to an Android key code. The key text is a name from the key list.
/// A name in the key list has priority over any other form of key text.
/// </summary>
public static class AndroidKeyCodes {
  /// <summary>The highest key code that a raw key code can have.</summary>
  public const int MaxRawKeyCode = 300;

  // Android KEYCODE_0 is 7 and KEYCODE_NUMPAD_0 is 144. The ten digits follow in order.
  private const int FirstDigitKeyCode = 7;
  private const int FirstNumberPadKeyCode = 144;

  private static readonly Dictionary<string, int> KeyNameMap = BuildKeyNameMap();

  private static Dictionary<string, int> BuildKeyNameMap() {
    var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase) {
      ["HOME"] = 3,
      ["BACK"] = 4,
      ["ESCAPE"] = 111, // KEYCODE_ESCAPE
      ["MENU"] = 82,
      ["ENTER"] = 66,
      ["SPACE"] = 62,
      ["TAB"] = 61,
      ["DEL"] = 67,
      ["DELETE"] = 67,
      ["UP"] = 19,
      ["DOWN"] = 20,
      ["LEFT"] = 21,
      ["RIGHT"] = 22,
      ["VOLUME_UP"] = 24,
      ["VOLUME_DOWN"] = 25,
      ["POWER"] = 26
    };
    for (var i = 0; i < 26; i++) {
      map[((char)('A' + i)).ToString()] = 29 + i; // KEYCODE_A is 29
    }
    for (var digit = 0; digit <= 9; digit++) {
      var text = digit.ToString(CultureInfo.InvariantCulture);
      map[text] = FirstDigitKeyCode + digit;
      map["NUMPAD_" + text] = FirstNumberPadKeyCode + digit;
    }
    return map;
  }

  /// <summary>Finds the Android key code of a key text.</summary>
  /// <param name="text">The key text. A listed name is matched without case, after the spaces at the start and end are removed.</param>
  /// <param name="keyCode">The key code when the result is <see cref="KeyResolveResult.Ok"/>. Otherwise 0.</param>
  /// <returns>
  /// <see cref="KeyResolveResult.Ok"/> when a key code was found. <see cref="KeyResolveResult.Empty"/> when the text is empty.
  /// <see cref="KeyResolveResult.UnknownName"/> when the text is not a known key. <see cref="KeyResolveResult.OutOfRange"/> when the text is a number that is not a valid key code.
  /// </returns>
  public static KeyResolveResult TryResolve(string? text, out int keyCode) {
    keyCode = 0;
    if (string.IsNullOrWhiteSpace(text)) return KeyResolveResult.Empty;
    var trimmed = text.Trim();
    if (KeyNameMap.TryGetValue(trimmed, out var code)) {
      keyCode = code;
      return KeyResolveResult.Ok;
    }
    if (!IsAsciiDigitsOnly(trimmed)) return KeyResolveResult.UnknownName;

    // A number too large to read is out of range as well. Only ASCII digits reach this line.
    if (!int.TryParse(trimmed, NumberStyles.None, CultureInfo.InvariantCulture, out var raw) || raw > MaxRawKeyCode) {
      return KeyResolveResult.OutOfRange;
    }
    keyCode = raw;
    return KeyResolveResult.Ok;
  }

  private static bool IsAsciiDigitsOnly(string text) {
    foreach (var c in text) {
      if (c is < '0' or > '9') return false;
    }
    return true;
  }
}
