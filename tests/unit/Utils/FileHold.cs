using System;
using System.IO;
using System.Threading.Tasks;

#pragma warning disable CA1707, CA2007, CA1849, CA2000, CA1031

namespace GameBot.UnitTests.Utils;

/// <summary>Holds a file open without delete share, as a reader or a scanner does (issue #333).</summary>
internal static class FileHold {
  /// <summary>Opens the file for read with <see cref="FileShare.Read"/> only.</summary>
  public static FileStream Open(string path) => new(path, FileMode.Open, FileAccess.Read, FileShare.Read);

  /// <summary>Closes the stream after a delay, on a worker thread.</summary>
  public static Task ReleaseAfter(FileStream stream, int delayMs) => Task.Run(async () => {
    await Task.Delay(delayMs);
    stream.Dispose();
  });

  /// <summary>A new temporary folder.</summary>
  public static string NewFolder() {
    var path = Path.Combine(Path.GetTempPath(), "GameBotAtomic", Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(path);
    return path;
  }
}
