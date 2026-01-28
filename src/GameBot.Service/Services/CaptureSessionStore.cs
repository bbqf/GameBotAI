using System.Collections.Concurrent;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.Versioning;

namespace GameBot.Service.Services;

internal sealed record CaptureSession(string Id, byte[] Png, int Width, int Height, DateTimeOffset CreatedAtUtc);

[SupportedOSPlatform("windows")]
internal sealed class CaptureSessionStore
{
    private readonly ConcurrentDictionary<string, CaptureSession> _captures = new();
    private readonly int _maxEntries;

    public CaptureSessionStore(int maxEntries = 10)
    {
        _maxEntries = Math.Max(1, maxEntries);
    }

    public CaptureSession Add(byte[] png)
    {
        ArgumentNullException.ThrowIfNull(png);
        using var ms = new MemoryStream(png, writable: false);
        using var bmp = new Bitmap(ms);
        var capture = new CaptureSession(Guid.NewGuid().ToString("N"), png, bmp.Width, bmp.Height, DateTimeOffset.UtcNow);
        _captures[capture.Id] = capture;
        TrimIfNeeded();
        return capture;
    }

    public bool TryGet(string id, out CaptureSession? capture)
    {
        return _captures.TryGetValue(id, out capture);
    }

    private void TrimIfNeeded()
    {
        if (_captures.Count <= _maxEntries) return;
        var oldest = _captures.Values.OrderBy(c => c.CreatedAtUtc).Take(_captures.Count - _maxEntries).ToList();
        foreach (var entry in oldest)
        {
            _captures.TryRemove(entry.Id, out _);
        }
    }
}
