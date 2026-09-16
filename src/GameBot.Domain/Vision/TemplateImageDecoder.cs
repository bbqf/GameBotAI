using System;
using OpenCvSharp;

namespace GameBot.Domain.Vision {
  /// <summary>
  /// Decodes reference-image bytes into a <see cref="Mat"/> for template matching, keeping any
  /// transparency channel intact.
  /// </summary>
  /// <remarks>
  /// Every <b>template</b> decode must go through here. Decoding a template with
  /// <see cref="ImreadModes.Color"/> forces 3-channel BGR and silently discards the alpha channel,
  /// which is the whole mask an operator authored (feature 089, issue #190).
  /// <para>
  /// <b>Screenshots</b> are the opposite case and must keep using <see cref="ImreadModes.Color"/>:
  /// a screen frame carries no mask, and forcing 3 channels keeps the matcher's input shape
  /// predictable.
  /// </para>
  /// </remarks>
  public static class TemplateImageDecoder {
    /// <summary>
    /// Decodes <paramref name="bytes"/>, preserving an alpha channel when the image has one.
    /// </summary>
    /// <param name="bytes">Encoded image bytes (PNG, JPEG, ...).</param>
    /// <returns>
    /// An 8-bit Mat with 1, 3, or 4 channels. Anything the unchanged decode returns in another
    /// depth — a 16-bit PNG, say — falls back to the 3-channel decode used before this feature, so
    /// exotic images keep their existing behaviour rather than reaching the matcher in a shape it
    /// has never handled.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="bytes"/> is null.</exception>
    /// <exception cref="OpenCVException">The bytes are not a decodable image.</exception>
    public static Mat Decode(byte[] bytes) {
      ArgumentNullException.ThrowIfNull(bytes);

      var unchanged = Mat.FromImageData(bytes, ImreadModes.Unchanged);
      if (IsUsable(unchanged))
        return unchanged;

      unchanged.Dispose();
      return Mat.FromImageData(bytes, ImreadModes.Color);
    }

    private static bool IsUsable(Mat decoded) {
      if (decoded is null || decoded.Empty())
        return false;
      if (decoded.Depth() != (int)MatType.CV_8U)
        return false;
      var channels = decoded.Channels();
      return channels is 1 or 3 or 4;
    }
  }
}
