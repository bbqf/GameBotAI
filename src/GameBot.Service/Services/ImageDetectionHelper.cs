using System.Globalization;
using GameBot.Domain.Commands;
using GameBot.Domain.Triggers;

namespace GameBot.Service.Services;

/// <summary>
/// Shared single-shot template-detection cycle (screenshot → template-match → coordinate-resolve).
/// Extracted so both the <c>waitForImage</c> step in <see cref="CommandExecutor"/> and the
/// game-readiness probe run the identical detection logic.
/// </summary>
internal static class ImageDetectionHelper {
  public static bool TryDetect(
    GameBot.Domain.Triggers.Evaluators.IScreenSource screenSrc,
    System.Drawing.Bitmap templateBmp,
    DetectionTarget detectionTarget,
    GameBot.Domain.Vision.ITemplateMatcher matcher,
    out PrimitiveTapResolvedPoint? resolvedPoint,
    out double? detectionConfidence) {
    resolvedPoint = null;
    detectionConfidence = null;

    var screenshotBmp = screenSrc.GetLatestScreenshot();
    if (screenshotBmp is null) {
      return false;
    }

    using var template = new System.Drawing.Bitmap(templateBmp);
    using var screenMs = new System.IO.MemoryStream();
    using var templateMs = new System.IO.MemoryStream();
    screenshotBmp.Save(screenMs, System.Drawing.Imaging.ImageFormat.Png);
    template.Save(templateMs, System.Drawing.Imaging.ImageFormat.Png);
    using var screenMat = OpenCvSharp.Mat.FromImageData(screenMs.ToArray(), OpenCvSharp.ImreadModes.Color);
    using var templateMat = OpenCvSharp.Mat.FromImageData(templateMs.ToArray(), OpenCvSharp.ImreadModes.Color);

    var adapter = new GameBot.Domain.Services.ActionExecutionAdapter(matcher);
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
