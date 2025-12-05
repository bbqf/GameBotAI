namespace GameBot.Service.Services.Detections
{
    internal sealed class DetectionOptions
    {
        public const string SectionName = "Service:Detections";

        public double Threshold { get; set; } = 0.8; // [0,1]
        public int MaxResults { get; set; } = 5;
        public int TimeoutMs { get; set; } = 500;
        public double Overlap { get; set; } = 0.45; // NMS IoU
    }
}
