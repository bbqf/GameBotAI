using GameBot.Domain.Commands;
using GameBot.Domain.Config;
using GameBot.Domain.Triggers.Evaluators;
using GameBot.Domain.Vision;

namespace GameBot.Service.Services.EnsureGameRunning;

internal sealed class GameReadinessProbe : IGameReadinessProbe {
  private readonly IScreenSource _screen;
  private readonly IReferenceImageStore _images;
  private readonly ITemplateMatcher _matcher;
  private readonly AppConfig _appConfig;

  public GameReadinessProbe(IScreenSource screen, IReferenceImageStore images, ITemplateMatcher matcher, AppConfig appConfig) {
    _screen = screen;
    _images = images;
    _matcher = matcher;
    _appConfig = appConfig;
  }

  public async Task<GameReadinessResult> WaitUntilReadyAsync(DetectionTarget readinessImage, int timeoutMs, CancellationToken ct = default) {
    if (!OperatingSystem.IsWindows()) {
      return new GameReadinessResult(false, "unavailable");
    }

    if (!_images.TryGet(readinessImage.ReferenceImageId, out var templateBmp) || templateBmp is null) {
      return new GameReadinessResult(false, "missing");
    }

    var deadline = DateTimeOffset.UtcNow.AddMilliseconds(Math.Max(0, timeoutMs));

    if (ImageDetectionHelper.TryDetect(_screen, templateBmp, readinessImage, _matcher, out _, out _)) {
      return new GameReadinessResult(true, "loaded");
    }

    while (DateTimeOffset.UtcNow < deadline) {
      var remaining = deadline - DateTimeOffset.UtcNow;
      if (remaining <= TimeSpan.Zero) {
        break;
      }

      var pollMs = Math.Max(1, Math.Min(_appConfig.CaptureIntervalMs, (int)Math.Ceiling(remaining.TotalMilliseconds)));
      await Task.Delay(pollMs, ct).ConfigureAwait(false);

      if (ImageDetectionHelper.TryDetect(_screen, templateBmp, readinessImage, _matcher, out _, out _)) {
        return new GameReadinessResult(true, "loaded");
      }
    }

    return new GameReadinessResult(false, "loaded");
  }
}
