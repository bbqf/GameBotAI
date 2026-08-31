using GameBot.Domain.Commands;
using GameBot.Domain.Config;
using GameBot.Domain.Triggers.Evaluators;
using GameBot.Domain.Vision;

namespace GameBot.Service.Services.EnsureGameRunning;

internal sealed class GameReadinessProbe : IGameReadinessProbe {
  private readonly IScreenSource _screen;
  // Feature 079: when the caller names its session, the probe observes only that device, so a
  // concurrent queue run's ready screen can never satisfy this gate. Null in stub/test wiring.
  private readonly IScreenSourceFactory? _screenFactory;
  private readonly IReferenceImageStore _images;
  private readonly ITemplateMatcher _matcher;
  private readonly AppConfig _appConfig;

  public GameReadinessProbe(IScreenSource screen, IReferenceImageStore images, ITemplateMatcher matcher, AppConfig appConfig, IScreenSourceFactory? screenFactory = null) {
    _screen = screen;
    _images = images;
    _matcher = matcher;
    _appConfig = appConfig;
    _screenFactory = screenFactory;
  }

  public async Task<GameReadinessResult> WaitUntilReadyAsync(DetectionTarget readinessImage, int timeoutMs, string? sessionId = null, CancellationToken ct = default) {
    var screen = _screenFactory is not null && !string.IsNullOrWhiteSpace(sessionId)
      ? _screenFactory.ForSession(sessionId)
      : _screen;

    if (!OperatingSystem.IsWindows()) {
      return new GameReadinessResult(false, "unavailable");
    }

    if (!_images.TryGet(readinessImage.ReferenceImageId, out var templateBmp) || templateBmp is null) {
      return new GameReadinessResult(false, "missing");
    }

    var deadline = DateTimeOffset.UtcNow.AddMilliseconds(Math.Max(0, timeoutMs));

    if (ImageDetectionHelper.TryDetect(screen, templateBmp, readinessImage, _matcher, out _, out _)) {
      return new GameReadinessResult(true, "loaded");
    }

    while (DateTimeOffset.UtcNow < deadline) {
      var remaining = deadline - DateTimeOffset.UtcNow;
      if (remaining <= TimeSpan.Zero) {
        break;
      }

      var pollMs = Math.Max(1, Math.Min(_appConfig.CaptureIntervalMs, (int)Math.Ceiling(remaining.TotalMilliseconds)));
      await Task.Delay(pollMs, ct).ConfigureAwait(false);

      if (ImageDetectionHelper.TryDetect(screen, templateBmp, readinessImage, _matcher, out _, out _)) {
        return new GameReadinessResult(true, "loaded");
      }
    }

    return new GameReadinessResult(false, "loaded");
  }
}
