using System.Drawing;

namespace GameBot.Domain.Profiles.Evaluators;

internal sealed class MemoryReferenceImageStore : IReferenceImageStore
{
    private readonly Dictionary<string, Bitmap> _images = new(StringComparer.OrdinalIgnoreCase);
    public void Add(string id, Bitmap bmp) => _images[id] = bmp;
    public bool TryGet(string id, out Bitmap bmp) => _images.TryGetValue(id, out bmp);
}

internal sealed class SingleBitmapScreenSource : IScreenSource
{
    private readonly Func<Bitmap?> _provider;
    public SingleBitmapScreenSource(Func<Bitmap?> provider) => _provider = provider;
    public Bitmap? GetLatestScreenshot() => _provider();
}
