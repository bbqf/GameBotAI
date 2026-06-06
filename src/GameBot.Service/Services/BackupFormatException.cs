namespace GameBot.Service.Services;

/// <summary>
/// Thrown when an uploaded archive is not a valid GameBot backup:
/// missing manifest, unsupported version, or referenced images absent from the archive.
/// </summary>
internal sealed class BackupFormatException : Exception {
  public BackupFormatException() { }
  public BackupFormatException(string message) : base(message) { }
  public BackupFormatException(string message, Exception innerException) : base(message, innerException) { }
}
