using System;
using System.IO;
using System.Linq;

namespace GameBot.Service.Endpoints
{
    internal static class ImageDetectionsValidation
    {
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
        public static bool ValidateContentType(string? value)
        {
            if (string.IsNullOrWhiteSpace(value)) return false;
            var normalized = NormalizeContentType(value);
            return AllowedContentTypes.Contains(normalized, StringComparer.OrdinalIgnoreCase);
        }

        public static bool TryNormalizeContentType(string? contentType, string? fileName, ReadOnlySpan<byte> data, out string normalized)
        {
            normalized = NormalizeContentType(contentType);

            if (AllowedContentTypes.Contains(normalized, StringComparer.OrdinalIgnoreCase))
            {
                return true;
            }

            if (LooksLikePng(data))
            {
                normalized = "image/png";
                return true;
            }

            if (LooksLikeJpeg(data))
            {
                normalized = "image/jpeg";
                return true;
            }

            if (!string.IsNullOrWhiteSpace(fileName))
            {
                var ext = Path.GetExtension(fileName).ToLowerInvariant();
                if (ext is ".png")
                {
                    normalized = "image/png";
                    return true;
                }
                if (ext is ".jpg" or ".jpeg")
                {
                    normalized = "image/jpeg";
                    return true;
                }
            }

            if (!data.IsEmpty)
            {
                normalized = "image/png";
                return true;
            }

            normalized = string.Empty;
            return false;
        }

        private static string NormalizeContentType(string? value)
        {
            if (string.IsNullOrWhiteSpace(value)) return string.Empty;
            var semi = value.IndexOf(';', StringComparison.Ordinal);
            return semi >= 0 ? value[..semi].Trim() : value.Trim();
        }

        private static bool LooksLikePng(ReadOnlySpan<byte> data)
        {
            // PNG signature: 89 50 4E 47 0D 0A 1A 0A
            if (data.Length < 8) return false;
            return data[0] == 0x89 && data[1] == 0x50 && data[2] == 0x4E && data[3] == 0x47 &&
                   data[4] == 0x0D && data[5] == 0x0A && data[6] == 0x1A && data[7] == 0x0A;
        }

        private static bool LooksLikeJpeg(ReadOnlySpan<byte> data)
        {
            // JPEG starts with FF D8 and ends with FF D9
            if (data.Length < 4) return false;
            return data[0] == 0xFF && data[1] == 0xD8 && data[^2] == 0xFF && data[^1] == 0xD9;
        }
        public static bool ValidateContentLength(long length) => length > 0 && length <= MaxImageBytes;

        public static (bool ok, string? error) ValidateRequest(GameBot.Service.Endpoints.Dto.DetectRequest req)
        {
            if (req is null) return (false, "invalid_request");
            if (string.IsNullOrWhiteSpace(req.ReferenceImageId)) return (false, "invalid_request: referenceImageId");
            if (req.Threshold is double t && !ValidateThreshold(t)) return (false, "invalid_request: threshold");
            if (req.Overlap is double o && !ValidateOverlap(o)) return (false, "invalid_request: overlap");
            if (req.MaxResults is int m && !ValidateMaxResults(m)) return (false, "invalid_request: maxResults");
            return (true, null);
        }
    }
}
