using System.Buffers;
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace GameBot.Domain.Gamepacks.Signing;

/// <summary>
/// Writes JSON in one fixed form, so equal content gives equal bytes (feature 146). Keys are sorted by ordinal compare.
/// No white space is written. The string and number rules are in the signature file contract.
/// </summary>
public static class CanonicalJson {
  /// <summary>Gives the canonical UTF-8 bytes of a JSON value.</summary>
  /// <param name="element">The value.</param>
  public static byte[] ToBytes(JsonElement element) {
    var writer = new ArrayBufferWriter<byte>();
    Write(element, writer);
    return writer.WrittenSpan.ToArray();
  }

  /// <summary>
  /// Gives the canonical UTF-8 bytes of a JSON object without some members of its first level (feature 149). The names are
  /// compared without case. A value that is not an object gives the same bytes as <see cref="ToBytes(JsonElement)"/>.
  /// </summary>
  /// <param name="element">The value.</param>
  /// <param name="skipRootMembers">The names of the first-level members to leave out.</param>
  public static byte[] ToBytes(JsonElement element, IReadOnlySet<string> skipRootMembers) {
    ArgumentNullException.ThrowIfNull(skipRootMembers);
    var writer = new ArrayBufferWriter<byte>();
    if (element.ValueKind == JsonValueKind.Object) {
      WriteObject(element, writer, skipRootMembers);
    }
    else {
      Write(element, writer);
    }

    return writer.WrittenSpan.ToArray();
  }

  /// <summary>Gives the canonical text of a JSON value.</summary>
  /// <param name="element">The value.</param>
  public static string ToText(JsonElement element) => Encoding.UTF8.GetString(ToBytes(element));

  /// <summary>Writes a JSON value in canonical form.</summary>
  /// <param name="element">The value.</param>
  /// <param name="output">The output.</param>
  public static void Write(JsonElement element, IBufferWriter<byte> output) {
    ArgumentNullException.ThrowIfNull(output);
    switch (element.ValueKind) {
      case JsonValueKind.Object:
        WriteObject(element, output);
        break;
      case JsonValueKind.Array:
        WriteArray(element, output);
        break;
      case JsonValueKind.String:
        WriteString(output, element.GetString() ?? string.Empty);
        break;
      case JsonValueKind.Number:
        WriteAscii(output, FormatNumber(element));
        break;
      case JsonValueKind.True:
        WriteAscii(output, "true");
        break;
      case JsonValueKind.False:
        WriteAscii(output, "false");
        break;
      default:
        WriteAscii(output, "null");
        break;
    }
  }

  private static void WriteObject(JsonElement element, IBufferWriter<byte> output, IReadOnlySet<string>? skip = null) {
    WriteAscii(output, "{");
    var first = true;
    foreach (var property in element.EnumerateObject().Where(p => skip is null || !skip.Contains(p.Name)).OrderBy(p => p.Name, StringComparer.Ordinal)) {
      if (!first) {
        WriteAscii(output, ",");
      }

      first = false;
      WriteString(output, property.Name);
      WriteAscii(output, ":");
      Write(property.Value, output);
    }

    WriteAscii(output, "}");
  }

  private static void WriteArray(JsonElement element, IBufferWriter<byte> output) {
    WriteAscii(output, "[");
    var first = true;
    foreach (var item in element.EnumerateArray()) {
      if (!first) {
        WriteAscii(output, ",");
      }

      first = false;
      Write(item, output);
    }

    WriteAscii(output, "]");
  }

  private static string FormatNumber(JsonElement element) {
    var raw = element.GetRawText();
    if (raw.AsSpan().IndexOfAny('.', 'e', 'E') < 0 && long.TryParse(raw, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var whole)) {
      return whole.ToString(CultureInfo.InvariantCulture);
    }

    var value = element.GetDouble();
    if (value == Math.Floor(value) && Math.Abs(value) < 1e15) {
      return ((long)value).ToString(CultureInfo.InvariantCulture);
    }

    return value.ToString("R", CultureInfo.InvariantCulture)
      .Replace("E+", "e", StringComparison.Ordinal)
      .Replace("E", "e", StringComparison.Ordinal);
  }

  private static void WriteAscii(IBufferWriter<byte> output, string text) {
    var span = output.GetSpan(text.Length);
    for (var i = 0; i < text.Length; i++) {
      span[i] = (byte)text[i];
    }

    output.Advance(text.Length);
  }

  private static void WriteString(IBufferWriter<byte> output, string value) {
    var builder = new StringBuilder(value.Length + 2);
    builder.Append('"');
    foreach (var c in value) {
      switch (c) {
        case '"':
          builder.Append("\\\"");
          break;
        case '\\':
          builder.Append("\\\\");
          break;
        case '\b':
          builder.Append("\\b");
          break;
        case '\f':
          builder.Append("\\f");
          break;
        case '\n':
          builder.Append("\\n");
          break;
        case '\r':
          builder.Append("\\r");
          break;
        case '\t':
          builder.Append("\\t");
          break;
        default:
          if (c < 0x20) {
            builder.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
          }
          else {
            builder.Append(c);
          }

          break;
      }
    }

    builder.Append('"');
    var text = builder.ToString();
    var count = Encoding.UTF8.GetByteCount(text);
    var span = output.GetSpan(count);
    Encoding.UTF8.GetBytes(text, span);
    output.Advance(count);
  }
}
