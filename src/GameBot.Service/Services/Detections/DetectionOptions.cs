namespace GameBot.Service.Services.Detections
{
    internal sealed class DetectionOptions
    {
        public const string SectionName = "Service:Detections";

        public double Threshold { get; set; } = GameBot.Service.Endpoints.ImageDetectionsValidation.DefaultThreshold; // [0,1]
        public int MaxResults { get; set; } = GameBot.Service.Endpoints.ImageDetectionsValidation.DefaultMaxResults;
        public int TimeoutMs { get; set; } = 500;
        public double Overlap { get; set; } = GameBot.Service.Endpoints.ImageDetectionsValidation.DefaultOverlap; // NMS IoU
    }
}
