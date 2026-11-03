namespace GameBot.Domain.Commands.Execution;

/// <summary>
/// The values of the <c>until</c> option of a <c>WaitForImage</c> step (feature 175).
/// <c>present</c> ends the wait when the image shows. <c>absent</c> ends the wait when the image is gone.
/// </summary>
public static class WaitForImageUntil {
  /// <summary>The wait ends when the image is seen. This is the default.</summary>
  public const string Present = "present";

  /// <summary>The wait ends when the image is not seen for <c>absentCaptures</c> captures in a row.</summary>
  public const string Absent = "absent";

  /// <summary>The default number of consecutive captures without the image.</summary>
  public const int DefaultAbsentCaptures = 2;

  /// <summary>
  /// Reads the <c>until</c> value. No value gives <c>present</c>. The case of the text does not matter.
  /// </summary>
  /// <param name="value">The text of the option, or null.</param>
  /// <param name="isAbsent">True when the value is <c>absent</c>.</param>
  /// <returns>True when the value is null, <c>present</c>, or <c>absent</c>. False for any other text.</returns>
  public static bool TryParse(string? value, out bool isAbsent) {
    isAbsent = false;
    if (value is null) return true;
    if (string.Equals(value, Present, StringComparison.OrdinalIgnoreCase)) return true;
    if (string.Equals(value, Absent, StringComparison.OrdinalIgnoreCase)) {
      isAbsent = true;
      return true;
    }

    return false;
  }
}
