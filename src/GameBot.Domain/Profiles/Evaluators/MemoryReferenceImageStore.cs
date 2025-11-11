using System.Drawing;
using System.Runtime.Versioning;

namespace GameBot.Domain.Profiles.Evaluators;

/// <summary>In-memory reference image store for image match templates.</summary>
[SupportedOSPlatform("windows")]
public sealed class MemoryReferenceImageStore : IReferenceImageStore
{
    private readonly Dictionary<string, Bitmap> _images = new(StringComparer.OrdinalIgnoreCase);
    public void Add(string id, Bitmap bmp)
    {
        ArgumentNullException.ThrowIfNull(id);
        ArgumentNullException.ThrowIfNull(bmp);
        _images[id] = bmp;
    }
    // Dictionary never stores null values; suppress nullable analysis with '!'
    public bool TryGet(string id, out Bitmap bmp)
    {
        if (id is null) { bmp = null!; return false; }
        return _images.TryGetValue(id, out bmp!);
    }
}

/// <summary>Screen source backed by a single bitmap provider (primarily for tests).</summary>
[SupportedOSPlatform("windows")]
public sealed class SingleBitmapScreenSource : IScreenSource
{
    private readonly Func<Bitmap?> _provider;
    public SingleBitmapScreenSource(Func<Bitmap?> provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        _provider = provider;
    }
    public Bitmap? GetLatestScreenshot() => _provider();
}
