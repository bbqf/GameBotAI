using System;

namespace GameBot.Service.Endpoints
{
    internal static class ImageDetectionsValidation
    {
        public static bool ValidateThreshold(double value) => value >= 0 && value <= 1;
        public static bool ValidateOverlap(double value) => value >= 0 && value <= 1;
        public static bool ValidateMaxResults(int value) => value >= 1 && value <= 100;

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
