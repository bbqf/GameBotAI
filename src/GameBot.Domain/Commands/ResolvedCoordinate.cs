namespace GameBot.Domain.Commands
{
    public sealed class ResolvedCoordinate
    {
        public int X { get; }
        public int Y { get; }
        public double Confidence { get; }
        public string SourceImageId { get; }
        public Vision.BoundingBox BBox { get; }

        public ResolvedCoordinate(int x, int y, double confidence, string sourceImageId, Vision.BoundingBox bbox)
        {
            X = x;
            Y = y;
            Confidence = confidence;
            SourceImageId = sourceImageId;
            BBox = bbox;
        }
    }
}
