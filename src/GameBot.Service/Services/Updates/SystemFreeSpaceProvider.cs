using System.Diagnostics.CodeAnalysis;

namespace GameBot.Service.Services.Updates;

/// <summary>Reads the free space of a drive with <see cref="DriveInfo"/>.</summary>
[ExcludeFromCodeCoverage]
internal sealed class SystemFreeSpaceProvider : IFreeSpaceProvider {
  public long GetFreeBytes(string path) {
    try {
      var root = Path.GetPathRoot(Path.GetFullPath(path));
      return string.IsNullOrEmpty(root) ? long.MaxValue : new DriveInfo(root).AvailableFreeSpace;
    }
    catch (Exception ex) when (ex is IOException or ArgumentException or UnauthorizedAccessException) {
      return long.MaxValue;
    }
  }
}
