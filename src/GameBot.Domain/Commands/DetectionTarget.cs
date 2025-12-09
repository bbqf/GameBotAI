using System;

namespace GameBot.Domain.Commands
{
    public sealed class DetectionTarget
    {
        public string ReferenceImageId { get; }
        public double Confidence { get; }
        public int OffsetX { get; }
        public int OffsetY { get; }
        public DetectionSelectionStrategy SelectionStrategy { get; }

        public DetectionTarget(string referenceImageId, double confidence = 0.8, int offsetX = 0, int offsetY = 0, DetectionSelectionStrategy selectionStrategy = DetectionSelectionStrategy.HighestConfidence)
        {
            if (string.IsNullOrWhiteSpace(referenceImageId))
                throw new ArgumentException("referenceImageId is required", nameof(referenceImageId));

            if (confidence < 0.0 || confidence > 1.0)
                throw new ArgumentOutOfRangeException(nameof(confidence), "confidence must be in [0,1]");

            ReferenceImageId = referenceImageId;
            Confidence = confidence;
            OffsetX = offsetX;
            OffsetY = offsetY;
            SelectionStrategy = selectionStrategy;
        }
    }
}
