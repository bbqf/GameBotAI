using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GameBot.Domain.Commands;
using GameBot.Domain.Gamepacks.Signing;

namespace GameBot.Domain.Gamepacks;

/// <summary>
/// Compares the content of two objects, and gives the content hash that the take-over backup stores (feature 149).
/// The canonical text of a sequence or a command is the canonical JSON (feature 146) of the object, without the members
/// that change at each write (<c>createdAt</c> and <c>updatedAt</c>). The id stays in the text, because the plan sets the
/// file object to the final id. An image is equal when the content type and the bytes are equal. The file name does not count.
/// </summary>
public static class GamepackContentComparer {
  private static readonly HashSet<string> VolatileMembers = new(new[] { "createdAt", "updatedAt" }, StringComparer.OrdinalIgnoreCase);

  /// <summary>Gives the canonical text of a sequence.</summary>
  /// <param name="sequence">The sequence.</param>
  public static string CanonicalText(CommandSequence sequence) => Encoding.UTF8.GetString(CanonicalBytes(sequence));

  /// <summary>Gives the canonical text of a command.</summary>
  /// <param name="command">The command.</param>
  public static string CanonicalText(Command command) => Encoding.UTF8.GetString(CanonicalBytes(command));

  /// <summary>True when the two sequences have the same canonical content.</summary>
  /// <param name="left">The first sequence.</param>
  /// <param name="right">The second sequence.</param>
  public static bool Identical(CommandSequence left, CommandSequence right) =>
    CanonicalBytes(left).AsSpan().SequenceEqual(CanonicalBytes(right));

  /// <summary>True when the two commands have the same canonical content.</summary>
  /// <param name="left">The first command.</param>
  /// <param name="right">The second command.</param>
  public static bool Identical(Command left, Command right) =>
    CanonicalBytes(left).AsSpan().SequenceEqual(CanonicalBytes(right));

  /// <summary>True when the two images have the same content type and the same bytes. The file name is ignored.</summary>
  /// <param name="leftContentType">The content type of the first image.</param>
  /// <param name="leftBytes">The bytes of the first image.</param>
  /// <param name="rightContentType">The content type of the second image.</param>
  /// <param name="rightBytes">The bytes of the second image.</param>
  public static bool ImagesIdentical(string? leftContentType, ReadOnlySpan<byte> leftBytes, string? rightContentType, ReadOnlySpan<byte> rightBytes) =>
    string.Equals(leftContentType, rightContentType, StringComparison.OrdinalIgnoreCase) && leftBytes.SequenceEqual(rightBytes);

  /// <summary>Gives the content hash of a sequence (SHA-256, lower-case hex).</summary>
  /// <param name="sequence">The sequence.</param>
  public static string Hash(CommandSequence sequence) => Convert.ToHexStringLower(SHA256.HashData(CanonicalBytes(sequence)));

  /// <summary>Gives the content hash of a command (SHA-256, lower-case hex).</summary>
  /// <param name="command">The command.</param>
  public static string Hash(Command command) => Convert.ToHexStringLower(SHA256.HashData(CanonicalBytes(command)));

  /// <summary>Gives the content hash of an image (SHA-256, lower-case hex) over the content type and the bytes.</summary>
  /// <param name="contentType">The content type.</param>
  /// <param name="bytes">The image bytes.</param>
  public static string HashImage(string? contentType, ReadOnlySpan<byte> bytes) {
    var type = Encoding.UTF8.GetBytes((contentType ?? string.Empty).ToUpperInvariant());
    var buffer = new byte[type.Length + 1 + bytes.Length];
    type.CopyTo(buffer, 0);
    buffer[type.Length] = 0;
    bytes.CopyTo(buffer.AsSpan(type.Length + 1));
    return Convert.ToHexStringLower(SHA256.HashData(buffer));
  }

  private static byte[] CanonicalBytes(CommandSequence sequence) {
    ArgumentNullException.ThrowIfNull(sequence);
    return CanonicalJson.ToBytes(JsonSerializer.SerializeToElement(sequence, GamepackFileSerializer.SequenceJsonOptions), VolatileMembers);
  }

  private static byte[] CanonicalBytes(Command command) {
    ArgumentNullException.ThrowIfNull(command);
    return CanonicalJson.ToBytes(JsonSerializer.SerializeToElement(command, GamepackFileSerializer.CommandJsonOptions), VolatileMembers);
  }
}
