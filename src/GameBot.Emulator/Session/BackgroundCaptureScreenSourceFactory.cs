using System;
using System.Runtime.Versioning;
using GameBot.Domain.Triggers.Evaluators;

namespace GameBot.Emulator.Session;

/// <summary>
/// Default <see cref="IScreenSourceFactory"/>: hands out <see cref="SessionScopedScreenSource"/>
/// instances over the shared background capture cache (feature 079).
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class BackgroundCaptureScreenSourceFactory : IScreenSourceFactory {
  private readonly BackgroundScreenCaptureService _captureService;

  /// <summary>Creates a factory over the per-session capture cache.</summary>
  public BackgroundCaptureScreenSourceFactory(BackgroundScreenCaptureService captureService) {
    ArgumentNullException.ThrowIfNull(captureService);
    _captureService = captureService;
  }

  /// <inheritdoc />
  /// <exception cref="ArgumentException"><paramref name="sessionId"/> is null, empty or whitespace.</exception>
  public IScreenSource ForSession(string sessionId) =>
    new SessionScopedScreenSource(_captureService, sessionId);
}
