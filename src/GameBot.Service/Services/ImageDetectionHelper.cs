using System.Globalization;
using GameBot.Domain.Commands;
using GameBot.Domain.Images;
using GameBot.Domain.Triggers;
using Microsoft.Extensions.Logging;

namespace GameBot.Service.Services;

/// <summary>
/// Shared single-shot template-detection cycle (screenshot → template-match → coordinate-resolve).
/// Extracted so both the <c>waitForImage</c> step in <see cref="CommandExecutor"/> and the
/// game-readiness probe run the identical detection logic.
/// </summary>
internal static class ImageDetectionHelper {
  /// <summary>
  /// Decodes a stored reference image for matching, preserving its transparency channel so a masked
  /// reference image masks here too (feature 089). Alternates use the same decode (feature 097).
  /// </summary>
  public static OpenCvSharp.Mat ToTemplateMat(System.Drawing.Bitmap bitmap) {
    using var template = new System.Drawing.Bitmap(bitmap);
    using var templateMs = new System.IO.MemoryStream();
    template.Save(templateMs, System.Drawing.Imaging.ImageFormat.Png);
    return GameBot.Domain.Vision.TemplateImageDecoder.Decode(templateMs.ToArray());
  }

  /// <summary>
  /// Runs one detection of <paramref name="references"/> — the named image and its alternates, any of
  /// which counts as a match (feature 097) — against the latest screenshot.
  /// </summary>
  public static bool TryDetect(
    GameBot.Domain.Triggers.Evaluators.IScreenSource screenSrc,
    ReferenceImageSet references,
    DetectionTarget detectionTarget,
    GameBot.Domain.Vision.ITemplateMatcher matcher,
    out PrimitiveTapResolvedPoint? resolvedPoint,
    out double? detectionConfidence,
    ILogger? logger = null) {
    ArgumentNullException.ThrowIfNull(references);
    resolvedPoint = null;
    detectionConfidence = null;

    var screenshotBmp = screenSrc.GetLatestScreenshot();
    if (screenshotBmp is null) {
      return false;
    }

    using var screenMs = new System.IO.MemoryStream();
    screenshotBmp.Save(screenMs, System.Drawing.Imaging.ImageFormat.Png);
    using var screenMat = OpenCvSharp.Mat.FromImageData(screenMs.ToArray(), OpenCvSharp.ImreadModes.Color);
    using var templateMat = ToTemplateMat(references.Primary);
    using var matcherLease = references.CreateMatcher(matcher, ToTemplateMat, logger);

    var adapter = new GameBot.Domain.Services.ActionExecutionAdapter(matcherLease.Matcher);
    var primitiveAction = new GameBot.Domain.Actions.InputAction {
      Type = "tap",
      Args = new Dictionary<string, object> { ["x"] = 0, ["y"] = 0 }
    };

    var ok = adapter.TryApplyDetectionCoordinates(
      primitiveAction,
      detectionTarget,
      screenMat,
      templateMat,
      detectionTarget.Confidence,
      out var err,
      DetectionSelectionStrategy.HighestConfidence);

    if (!ok || err is not null) {
      return false;
    }

    if (!primitiveAction.Args.TryGetValue("x", out var xVal) || !primitiveAction.Args.TryGetValue("y", out var yVal)) {
      return false;
    }

    var x = Convert.ToInt32(xVal, CultureInfo.InvariantCulture);
    var y = Convert.ToInt32(yVal, CultureInfo.InvariantCulture);
    if (x < 0 || y < 0 || x >= screenshotBmp.Width || y >= screenshotBmp.Height) {
      return false;
    }

    detectionConfidence = primitiveAction.Args.TryGetValue("confidence", out var confidenceVal)
      ? Convert.ToDouble(confidenceVal, CultureInfo.InvariantCulture)
      : (double?)null;
    resolvedPoint = new PrimitiveTapResolvedPoint(x, y);
    return true;
  }
}
