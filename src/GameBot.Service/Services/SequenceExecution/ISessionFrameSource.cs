using System.Drawing;

namespace GameBot.Service.Services.SequenceExecution;

/// <summary>
/// Captures the latest emulator frame for a specific session (feature 068). Abstracts the
/// background capture cache so <see cref="OcrOffsetResolver"/> stays unit-testable with a fake.
/// </summary>
internal interface ISessionFrameSource {
  /// <summary>Returns a detached copy of the latest frame, or null when none is available.</summary>
  Bitmap? Capture(string sessionId);
}
