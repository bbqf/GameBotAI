using System;
using System.IO;

namespace GameBot.Domain.Logging;

public static class AtomicFileWriter
{
    public static void WriteAllBytesAtomic(string finalPath, byte[] bytes)
    {
        var dir = Path.GetDirectoryName(finalPath)!;
        Directory.CreateDirectory(dir);
        var tmpDir = Path.Combine(dir, ".tmp");
        Directory.CreateDirectory(tmpDir);
        var tmpFile = Path.Combine(tmpDir, Path.GetFileName(finalPath) + "." + DateTime.UtcNow.Ticks + ".tmp");
        File.WriteAllBytes(tmpFile, bytes);
        if (File.Exists(finalPath))
        {
            // Replace atomically where supported; else delete then move
            try { File.Replace(tmpFile, finalPath, null); }
            catch { File.Delete(finalPath); File.Move(tmpFile, finalPath); }
        }
        else
        {
            File.Move(tmpFile, finalPath);
        }
    }
}