using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.Versioning;
using GameBot.Domain.Logging;

namespace GameBot.Domain.Triggers.Evaluators;

[SupportedOSPlatform("windows")]
public class ReferenceImageStore : IReferenceImageStore
{
    private readonly string _root;

    public ReferenceImageStore(string root)
    {
        _root = root;
        Directory.CreateDirectory(_root);
    }

    private string ResolvePath(string id)
    {
        return Path.Combine(_root, id + ".png");
    }

    public bool TryGet(string id, out Bitmap bitmap)
    {
        bitmap = default!;
        if (!ReferenceImageIdValidator.IsValid(id)) return false;
        var path = ResolvePath(id);
        if (!File.Exists(path)) return false;
        using var fs = File.OpenRead(path);
        using var bmp = new Bitmap(fs);
        bitmap = (Bitmap)bmp.Clone();
        return true;
    }

    public void AddOrUpdate(string id, Bitmap bitmap)
    {
        if (!ReferenceImageIdValidator.IsValid(id)) throw new ArgumentException("invalid id", nameof(id));
        ArgumentNullException.ThrowIfNull(bitmap);
        var path = ResolvePath(id);
        // Use temp file + direct bitmap.Save to reduce potential encoding issues
        var dir = Path.GetDirectoryName(path)!;
        Directory.CreateDirectory(dir);
        var tmp = Path.Combine(dir, ".tmp", id + ".png." + DateTime.UtcNow.Ticks + ".tmp");
        Directory.CreateDirectory(Path.GetDirectoryName(tmp)!);
        bitmap.Save(tmp, ImageFormat.Png);
        if (File.Exists(path)) {
            try { File.Replace(tmp, path, null); }
            catch { File.Delete(path); File.Move(tmp, path); }
        }
        else {
            File.Move(tmp, path);
        }
        if (!File.Exists(path)) throw new IOException($"Failed to persist reference image '{id}' to '{path}'");
    }

    public bool Exists(string id)
    {
        if (!ReferenceImageIdValidator.IsValid(id)) return false;
        return File.Exists(ResolvePath(id));
    }

    public bool Delete(string id)
    {
        if (!ReferenceImageIdValidator.IsValid(id)) return false;
        var path = ResolvePath(id);
        if (!File.Exists(path)) return false;
        File.Delete(path);
        return true;
    }
}