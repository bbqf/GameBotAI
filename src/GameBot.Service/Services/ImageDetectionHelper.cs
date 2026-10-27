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
    var pass = DetectPass(screenSrc, references, detectionTarget, matcher, logger);
    resolvedPoint = pass.ResolvedPoint;
    detectionConfidence = pass.DetectionConfidence;
    return pass.Found;
  }

  /// <summary>
  /// Runs one detection pass, as <see cref="TryDetect"/> does, and also tells if a frame was available
  /// and the best score at or above the matcher threshold (feature 166, FR-012). The
  /// <c>WaitForImage</c> step writes these values in its timeout log line.
  /// </summary>
  public static DetectionPassResult DetectPass(
    GameBot.Domain.Triggers.Evaluators.IScreenSource screenSrc,
    ReferenceImageSet references,
    DetectionTarget detectionTarget,
    GameBot.Domain.Vision.ITemplateMatcher matcher,
    ILogger? logger = null) {
    ArgumentNullException.ThrowIfNull(screenSrc);
    ArgumentNullException.ThrowIfNull(references);
    ArgumentNullException.ThrowIfNull(detectionTarget);

    var screenshotBmp = screenSrc.GetLatestScreenshot();
    if (screenshotBmp is null) {
      return DetectionPassResult.NoFrame;
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

    // The score of the selected match, when the matcher found one at or above its threshold.
    var bestScore = primitiveAction.Args.TryGetValue("confidence", out var confidenceVal)
      ? Convert.ToDouble(confidenceVal, CultureInfo.InvariantCulture)
      : (double?)null;

    if (!ok || err is not null) {
      return DetectionPassResult.NotFound(bestScore);
    }

    if (!primitiveAction.Args.TryGetValue("x", out var xVal) || !primitiveAction.Args.TryGetValue("y", out var yVal)) {
      return DetectionPassResult.NotFound(bestScore);
    }

    var x = Convert.ToInt32(xVal, CultureInfo.InvariantCulture);
    var y = Convert.ToInt32(yVal, CultureInfo.InvariantCulture);
    if (x < 0 || y < 0 || x >= screenshotBmp.Width || y >= screenshotBmp.Height) {
      return DetectionPassResult.NotFound(bestScore);
    }

    return new DetectionPassResult(true, true, bestScore, new PrimitiveTapResolvedPoint(x, y), bestScore);
  }
}

/// <summary>
/// The result of one detection pass (feature 166, FR-012). It is not stored and not in an API response.
/// </summary>
/// <param name="Found">A match at or above the step confidence was found inside the capture.</param>
/// <param name="FrameAvailable">The screen source gave a frame. False means that the session has no cached frame.</param>
/// <param name="BestScore">The best match score at or above the matcher threshold, or null.</param>
/// <param name="ResolvedPoint">The point with the offsets. Not null only when <paramref name="Found"/> is true.</param>
/// <param name="DetectionConfidence">The score of the selected match. Not null only when <paramref name="Found"/> is true.</param>
internal sealed record DetectionPassResult(
  bool Found,
  bool FrameAvailable,
  double? BestScore,
  PrimitiveTapResolvedPoint? ResolvedPoint,
  double? DetectionConfidence) {
  /// <summary>The result of a pass with no frame.</summary>
  public static DetectionPassResult NoFrame { get; } = new(false, false, null, null, null);

  /// <summary>The result of a pass with a frame and no match.</summary>
  public static DetectionPassResult NotFound(double? bestScore) => new(false, true, bestScore, null, null);
}
