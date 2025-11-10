using System.Drawing;
using System.Runtime.Versioning;

namespace GameBot.Domain.Profiles.Evaluators;

/// <summary>In-memory reference image store for image match templates.</summary>
[SupportedOSPlatform("windows")]
public sealed class MemoryReferenceImageStore : IReferenceImageStore
{
    private readonly Dictionary<string, Bitmap> _images = new(StringComparer.OrdinalIgnoreCase);
    public void Add(string id, Bitmap bmp) => _images[id] = bmp;
    // Dictionary never stores null values; suppress nullable analysis with '!'
    public bool TryGet(string id, out Bitmap bmp) => _images.TryGetValue(id, out bmp!);
}

/// <summary>Screen source backed by a single bitmap provider (primarily for tests).</summary>
[SupportedOSPlatform("windows")]
public sealed class SingleBitmapScreenSource : IScreenSource
{
    private readonly Func<Bitmap?> _provider;
    public SingleBitmapScreenSource(Func<Bitmap?> provider) => _provider = provider;
    public Bitmap? GetLatestScreenshot() => _provider();
}
