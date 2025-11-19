using System.Collections.Concurrent;
using System.Drawing;

namespace GameBot.Domain.Triggers.Evaluators;

public interface IReferenceImageStore
{
    void AddOrUpdate(string id, Bitmap bmp);
    bool TryGet(string id, out Bitmap bmp);
}

public sealed class MemoryReferenceImageStore : IReferenceImageStore
{
    private readonly ConcurrentDictionary<string, Bitmap> _images = new(StringComparer.OrdinalIgnoreCase);
    public void AddOrUpdate(string id, Bitmap bmp) => _images.AddOrUpdate(id, bmp, (_, __) => bmp);
    public bool TryGet(string id, out Bitmap bmp) => _images.TryGetValue(id, out bmp!);
}