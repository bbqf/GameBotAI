using System.Diagnostics.Metrics;

namespace GameBot.Service.Endpoints;

internal static class ImageReferencesMetrics
{
    private static readonly Meter Meter = new("GameBot.Service.Images", "1.0.0");
    private static readonly Counter<long> ListRequests = Meter.CreateCounter<long>("image_list_requests");
    private static readonly Counter<long> GetRequests = Meter.CreateCounter<long>("image_get_requests");
    private static readonly Counter<long> GetNotFound = Meter.CreateCounter<long>("image_get_not_found");
    private static readonly Histogram<long> GetBytes = Meter.CreateHistogram<long>("image_get_size_bytes");
    private static readonly Counter<long> DeleteSuccess = Meter.CreateCounter<long>("image_delete_success");
    private static readonly Counter<long> DeleteConflict = Meter.CreateCounter<long>("image_delete_conflict");

    public static void RecordListRequest(int count)
    {
        ListRequests.Add(1);
    }

    public static void RecordGet(bool found, long sizeBytes)
    {
        GetRequests.Add(1);
        if (!found)
        {
            GetNotFound.Add(1);
            return;
        }
        if (sizeBytes > 0) GetBytes.Record(sizeBytes);
    }

    public static void RecordDeleteSuccess()
    {
        DeleteSuccess.Add(1);
    }

    public static void RecordDeleteConflict(int blockers)
    {
        DeleteConflict.Add(blockers <= 0 ? 1 : blockers);
    }
}
