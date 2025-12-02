using System.Collections.Generic;

namespace GameBot.Service.Endpoints.Dto
{
    internal sealed class DetectRequest
    {
        public string? ReferenceImageId { get; set; }
        public double? Threshold { get; set; }
        public int? MaxResults { get; set; }
        public double? Overlap { get; set; }
    }

    internal sealed class DetectResponse
    {
        public System.Collections.ObjectModel.Collection<MatchResult> Matches { get; set; } = new();
        public bool LimitsHit { get; set; }
    }

    internal sealed class MatchResult
    {
        public NormalizedRect Bbox { get; set; } = new();
        public double Confidence { get; set; }
    }

    internal sealed class NormalizedRect
    {
        public double X { get; set; }
        public double Y { get; set; }
        public double Width { get; set; }
        public double Height { get; set; }
    }
}
