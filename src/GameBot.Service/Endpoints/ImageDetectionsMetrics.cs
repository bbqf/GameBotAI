using System.Diagnostics.Metrics;

namespace GameBot.Service.Endpoints {
  internal static class ImageDetectionsMetrics {
    private static readonly Meter Meter = new("GameBot.Service.ImageDetections", "1.0.0");
    private static readonly Histogram<long> DurationMs = Meter.CreateHistogram<long>("image_detections_duration_ms");
    private static readonly Counter<long> ResultCount = Meter.CreateCounter<long>("image_detections_result_count");

    public static void Record(long durationMs, int resultCount) {
      DurationMs.Record(durationMs);
      ResultCount.Add(resultCount);
    }
  }
}
