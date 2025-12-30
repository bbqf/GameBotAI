using System;
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
        public static readonly string[] AllowedContentTypes = new[] { "image/png", "image/jpeg" };

        public static bool ValidateThreshold(double value) => value >= 0 && value <= 1;
        public static bool ValidateOverlap(double value) => value >= 0 && value <= 1;
        public static bool ValidateMaxResults(int value) => value >= 1 && value <= MaxResultsLimit;
        public static bool ValidateContentType(string? value) => !string.IsNullOrWhiteSpace(value) && AllowedContentTypes.Contains(value, StringComparer.OrdinalIgnoreCase);
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
