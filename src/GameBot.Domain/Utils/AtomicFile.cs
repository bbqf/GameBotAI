using System.Globalization;

namespace GameBot.Domain.Utils;

/// <summary>
/// Replaces a file with the content of a temporary file. Used by the gamepack install path (issue #333).
/// </summary>
/// <remarks>
/// <see cref="File.Replace(string, string, string?)"/> fails on Windows when another handle holds the old file.
/// This helper uses a move with overwrite. It retries only when another handle holds the target
/// (sharing violation, lock violation, or access denied on a target that is not read-only).
/// </remarks>
public static class AtomicFile {
  private const int SharingViolation = 32;
  private const int LockViolation = 33;

  private static readonly int[] RetryDelaysMs = { 50, 100, 200, 400, 800 };

  /// <summary>
  /// Moves <paramref name="tempPath"/> over <paramref name="targetPath"/>. The target can be missing.
  /// </summary>
  /// <param name="tempPath">The temporary file with the new content.</param>
  /// <param name="targetPath">The file to replace.</param>
  /// <param name="kind">The object kind: sequence, command, image, template, registry or previous.</param>
  /// <param name="id">The object id, or the gamepack name for registry and previous.</param>
  /// <exception cref="IOException">
  /// The file cannot be replaced. The message is <see cref="FormatError"/>. The temporary file is deleted.
  /// </exception>
  public static void ReplaceFile(string tempPath, string targetPath, string kind, string id) {
    ArgumentException.ThrowIfNullOrWhiteSpace(tempPath);
    ArgumentException.ThrowIfNullOrWhiteSpace(targetPath);
    var fullTarget = Path.GetFullPath(targetPath);

    for (var attempt = 0; ; attempt++) {
      try {
        File.Move(tempPath, fullTarget, overwrite: true);
        return;
      }
      catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) {
        if (attempt < RetryDelaysMs.Length && IsRetryable(ex, fullTarget)) {
          Thread.Sleep(RetryDelaysMs[attempt]);
          continue;
        }

        TryDelete(tempPath);
        throw new IOException(FormatError(fullTarget, kind, id, ex.Message), ex);
      }
    }
  }

  /// <summary>Builds the fixed error text: <c>Cannot replace file '&lt;full path&gt;' for &lt;kind&gt; '&lt;id&gt;': &lt;reason&gt;</c>.</summary>
  /// <param name="fullPath">The full path of the target file.</param>
  /// <param name="kind">The object kind.</param>
  /// <param name="id">The object id.</param>
  /// <param name="reason">The reason text.</param>
  public static string FormatError(string fullPath, string kind, string id, string reason) =>
    string.Format(CultureInfo.InvariantCulture, "Cannot replace file '{0}' for {1} '{2}': {3}", fullPath, kind, id, reason);

  private static bool IsRetryable(Exception ex, string targetPath) {
    if (ex is IOException io) {
      var code = io.HResult & 0xFFFF;
      return code is SharingViolation or LockViolation;
    }

    // Windows reports "access denied" when another handle holds the target without delete share.
    // A read-only target gives the same error, but a retry cannot fix it.
    try {
      return File.Exists(targetPath) && (File.GetAttributes(targetPath) & FileAttributes.ReadOnly) == 0;
    }
    catch (Exception probe) when (probe is IOException or UnauthorizedAccessException) {
      return false;
    }
  }

  private static void TryDelete(string path) {
    try {
      File.Delete(path);
    }
    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) {
      // The temporary file stays. The error for the target file is more useful.
    }
  }
}
