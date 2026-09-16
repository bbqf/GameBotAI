using System;
using System.IO;
using System.Linq;

namespace GameBot.Service.Endpoints {
  internal static class ImageDetectionsValidation {
    // Defaults for detection when callers omit values
    public const double DefaultThreshold = 0.86;
    public const double DefaultOverlap = 0.1;
    public const int DefaultMaxResults = 1;

    // Limits
    public const int MaxImageBytes = 10_000_000;
    public const int MaxResultsLimit = 100;

    // Allowed mime types for uploads
    public static readonly string[] AllowedContentTypes = new[] { "image/png", "image/x-png", "image/jpeg", "image/jpg", "image/pjpeg" };

    public static bool ValidateThreshold(double value) => value >= 0 && value <= 1;
    public static bool ValidateOverlap(double value) => value >= 0 && value <= 1;
    public static bool ValidateMaxResults(int value) => value >= 1 && value <= MaxResultsLimit;
    public static bool ValidateContentType(string? value) {
      if (string.IsNullOrWhiteSpace(value)) return false;
      var normalized = NormalizeContentType(value);
      return AllowedContentTypes.Contains(normalized, StringComparer.OrdinalIgnoreCase);
    }

    public static bool TryNormalizeContentType(string? contentType, string? fileName, ReadOnlySpan<byte> data, out string normalized) {
      normalized = NormalizeContentType(contentType);

      if (AllowedContentTypes.Contains(normalized, StringComparer.OrdinalIgnoreCase)) {
        return true;
      }

      if (LooksLikePng(data)) {
        normalized = "image/png";
        return true;
      }

      if (LooksLikeJpeg(data)) {
        normalized = "image/jpeg";
        return true;
      }

      if (!string.IsNullOrWhiteSpace(fileName)) {
        var ext = Path.GetExtension(fileName).ToLowerInvariant();
        if (ext is ".png") {
          normalized = "image/png";
          return true;
        }
        if (ext is ".jpg" or ".jpeg") {
          normalized = "image/jpeg";
          return true;
        }
      }

      if (!data.IsEmpty) {
        normalized = "image/png";
        return true;
      }

      normalized = string.Empty;
      return false;
    }

    private static string NormalizeContentType(string? value) {
      if (string.IsNullOrWhiteSpace(value)) return string.Empty;
      var semi = value.IndexOf(';', StringComparison.Ordinal);
      return semi >= 0 ? value[..semi].Trim() : value.Trim();
    }

    private static bool LooksLikePng(ReadOnlySpan<byte> data) {
      // PNG signature: 89 50 4E 47 0D 0A 1A 0A
      if (data.Length < 8) return false;
      return data[0] == 0x89 && data[1] == 0x50 && data[2] == 0x4E && data[3] == 0x47 &&
             data[4] == 0x0D && data[5] == 0x0A && data[6] == 0x1A && data[7] == 0x0A;
    }

    private static bool LooksLikeJpeg(ReadOnlySpan<byte> data) {
      // JPEG starts with FF D8 and ends with FF D9
      if (data.Length < 4) return false;
      return data[0] == 0xFF && data[1] == 0xD8 && data[^2] == 0xFF && data[^1] == 0xD9;
    }
    public static bool ValidateContentLength(long length) => length > 0 && length <= MaxImageBytes;

    /// <summary>
    /// Rejects an upload whose transparency mask retains too few pixels to match anything
    /// meaningfully (feature 089, issue #190).
    /// </summary>
    /// <param name="bytes">The encoded upload.</param>
    /// <param name="retainedCount">The retained pixel count, when the image carries a mask.</param>
    /// <returns>True when the upload may proceed.</returns>
    /// <remarks>
    /// Only an image that is <i>actually masked</i> is inspected. An image with no alpha channel,
    /// and an image whose alpha is uniformly opaque, are both left alone — the latter matters,
    /// because an all-opaque alpha is not a mask and a small opaque image must keep uploading
    /// exactly as it always has.
    /// <para>
    /// A mask retaining a handful of pixels correlates with almost any patch of screen. Refusing it
    /// here is the only point where the operator can act on it; refusing it at detection time would
    /// reproduce the silent no-match this feature exists to remove.
    /// </para>
    /// </remarks>
    public static bool ValidateMaskRetention(byte[] bytes, out int retainedCount) {
      retainedCount = 0;
      if (bytes is null || bytes.Length == 0) return true;

      OpenCvSharp.Mat? decoded = null;
      try {
        decoded = GameBot.Domain.Vision.TemplateImageDecoder.Decode(bytes);
        if (!GameBot.Domain.Vision.TemplateMask.TryCreate(decoded, out var mask, out retainedCount))
          return true;
        mask.Dispose();
        return retainedCount >= GameBot.Domain.Vision.TemplateMask.MinimumRetainedPixels;
      }
      catch (OpenCvSharp.OpenCVException) {
        // Undecodable bytes are not this rule's business; content-type validation already ran and
        // the repository reports its own failure.
        return true;
      }
      finally {
        decoded?.Dispose();
      }
    }

    public static (bool ok, string? error) ValidateRequest(GameBot.Service.Endpoints.Dto.DetectRequest req) {
      if (req is null) return (false, "invalid_request");
      if (string.IsNullOrWhiteSpace(req.ReferenceImageId)) return (false, "invalid_request: referenceImageId");
      if (req.Threshold is double t && !ValidateThreshold(t)) return (false, "invalid_request: threshold");
      if (req.Overlap is double o && !ValidateOverlap(o)) return (false, "invalid_request: overlap");
      if (req.MaxResults is int m && !ValidateMaxResults(m)) return (false, "invalid_request: maxResults");
      // Feature 085: the two detection targets name different screens, so supplying both is
      // ambiguous. Rejecting it is the whole point of the feature — never guess which one was meant.
      // Blank values count as absent, so a client sending "" stays on the implicit path unchanged.
      if (!string.IsNullOrWhiteSpace(req.CaptureId) && !string.IsNullOrWhiteSpace(req.SessionId)) {
        return (false, "invalid_request: captureId and sessionId are mutually exclusive");
      }
      return (true, null);
    }
  }
}
