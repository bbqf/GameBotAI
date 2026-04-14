using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.Versioning;
using GameBot.Domain.Vision;
using Microsoft.Extensions.Logging;
using OpenCvSharp;

namespace GameBot.Domain.Triggers.Evaluators;

[SupportedOSPlatform("windows")]
public sealed class ImageMatchEvaluator : ITriggerEvaluator {
  private readonly IReferenceImageStore _store;
  private readonly IScreenSource _screen;
  private readonly ITemplateMatcher _matcher;
  private readonly ILogger<ImageMatchEvaluator>? _logger;

  public ImageMatchEvaluator(IReferenceImageStore store, IScreenSource screen, ITemplateMatcher matcher, ILogger<ImageMatchEvaluator>? logger = null) {
    _store = store;
    _screen = screen;
    _matcher = matcher;
    _logger = logger;
  }

  // Backward-compatible constructor for existing tests that don't supply ITemplateMatcher
  public ImageMatchEvaluator(IReferenceImageStore store, IScreenSource screen)
    : this(store, screen, FallbackMatcher.Instance) { }

  public bool CanEvaluate(Trigger trigger) {
    ArgumentNullException.ThrowIfNull(trigger);
    return trigger.Enabled && trigger.Type == TriggerType.ImageMatch;
  }

  public TriggerEvaluationResult Evaluate(Trigger trigger, DateTimeOffset now) {
    ArgumentNullException.ThrowIfNull(trigger);
    var p = (ImageMatchParams)trigger.Params;
    var sw = Stopwatch.StartNew();
    var similarity = ComputeSimilarity(p);
    sw.Stop();
    if (_logger is not null && sw.ElapsedMilliseconds > 50)
    {
      ImageMatchLog.SlowMatch(_logger, p.ReferenceImageId, sw.ElapsedMilliseconds, null);
    }
    var status = similarity >= p.SimilarityThreshold ? TriggerStatus.Satisfied : TriggerStatus.Pending;
    return new TriggerEvaluationResult {
      Status = status,
      Similarity = similarity,
      EvaluatedAt = now,
      Reason = status == TriggerStatus.Satisfied ? "similarity_met" : "similarity_below_threshold"
    };
  }

  private double ComputeSimilarity(ImageMatchParams p) {
    if (!_store.TryGet(p.ReferenceImageId, out var tpl)) return 0d;
    using var screenBmp = _screen.GetLatestScreenshot();
    if (screenBmp is null) return 0d;

    // Compute pixel region from normalized coordinates
    var rx = (int)Math.Round(p.Region.X * screenBmp.Width);
    var ry = (int)Math.Round(p.Region.Y * screenBmp.Height);
    var rw = Math.Max(1, (int)Math.Round(p.Region.Width * screenBmp.Width));
    var rh = Math.Max(1, (int)Math.Round(p.Region.Height * screenBmp.Height));
    rx = Math.Clamp(rx, 0, Math.Max(0, screenBmp.Width - 1));
    ry = Math.Clamp(ry, 0, Math.Max(0, screenBmp.Height - 1));
    rw = Math.Clamp(rw, 1, screenBmp.Width - rx);
    rh = Math.Clamp(rh, 1, screenBmp.Height - ry);

    if (tpl.Width > rw || tpl.Height > rh) return 0d;

    // Convert Bitmaps to OpenCV Mats and use ITemplateMatcher (hardware-accelerated)
    using var screenMat = OpenCvSharp.Extensions.BitmapConverter.ToMat(screenBmp);
    using var regionMat = screenMat.SubMat(ry, ry + rh, rx, rx + rw);
    using var tplMat = OpenCvSharp.Extensions.BitmapConverter.ToMat(tpl);

    using var grayTpl = tplMat.Channels() == 1 ? tplMat.Clone() : tplMat.CvtColor(ColorConversionCodes.BGR2GRAY);
    using var grayRegion = regionMat.Channels() == 1 ? regionMat.Clone() : regionMat.CvtColor(ColorConversionCodes.BGR2GRAY);

    // CCoeffNormed degenerates when template or region is constant (zero variance after mean subtraction).
    // Also when template is exactly region-sized, the result mat is 1×1 and always 1.0.
    // In these cases, fall back to direct mean-absolute-difference comparison.
    Cv2.MeanStdDev(grayTpl, out var tplMean, out var tplStdDev);
    bool tplIsConstant = tplStdDev.Val0 < 1.0;
    if (tplIsConstant || (tpl.Width == rw && tpl.Height == rh))
    {
      // For same-size or constant-template: compute mean absolute difference
      // If sizes differ, resize region to template size for comparison
      using var compareRegion = (tpl.Width == rw && tpl.Height == rh)
        ? grayRegion.Clone()
        : grayRegion.Resize(new OpenCvSharp.Size(grayTpl.Width, grayTpl.Height));
      using var diff = new Mat();
      Cv2.Absdiff(compareRegion, grayTpl, diff);
      var meanDiff = Cv2.Mean(diff);
      // Normalize: 0 diff → 1.0 similarity, 255 diff → 0.0
      return Math.Max(0, 1.0 - (meanDiff.Val0 / 255.0));
    }

    var config = new TemplateMatcherConfig(Threshold: 0.0, MaxResults: 1, Overlap: 1.0);
#pragma warning disable CA2025 // Dispose is not premature — GetResult() blocks synchronously
    var result = _matcher.MatchAllAsync(regionMat, tplMat, config, CancellationToken.None)
        .ConfigureAwait(false).GetAwaiter().GetResult();
#pragma warning restore CA2025

    if (result.Matches.Count == 0) return 0d;
    return Math.Max(0, Math.Min(1, result.Matches[0].Confidence));
  }

  /// <summary>Fallback that uses the old pure-C# NCC when ITemplateMatcher is not available (tests).</summary>
  private sealed class FallbackMatcher : ITemplateMatcher {
    public static readonly FallbackMatcher Instance = new();
    public Task<TemplateMatchResult> MatchAllAsync(Mat screenshot, Mat templateMat, TemplateMatcherConfig config, CancellationToken cancellationToken = default) {
      // Delegate to OpenCV directly — this path is only hit in unit tests that don't supply a matcher
      if (screenshot.Empty() || templateMat.Empty())
        return Task.FromResult(new TemplateMatchResult(Array.Empty<Vision.TemplateMatch>(), false));
      using var graySrc = screenshot.Channels() == 1 ? screenshot.Clone() : screenshot.CvtColor(ColorConversionCodes.BGR2GRAY);
      using var grayTpl = templateMat.Channels() == 1 ? templateMat.Clone() : templateMat.CvtColor(ColorConversionCodes.BGR2GRAY);
      var resultRows = graySrc.Rows - grayTpl.Rows + 1;
      var resultCols = graySrc.Cols - grayTpl.Cols + 1;
      if (resultRows <= 0 || resultCols <= 0)
        return Task.FromResult(new TemplateMatchResult(Array.Empty<Vision.TemplateMatch>(), false));
      using var result = new Mat(resultRows, resultCols, MatType.CV_32FC1);
      Cv2.MatchTemplate(graySrc, grayTpl, result, TemplateMatchModes.CCoeffNormed);
      result.MinMaxLoc(out _, out double maxVal, out _, out OpenCvSharp.Point maxLoc);
      if (maxVal < config.Threshold)
        return Task.FromResult(new TemplateMatchResult(Array.Empty<Vision.TemplateMatch>(), false));
      var bbox = new BoundingBox(maxLoc.X, maxLoc.Y, grayTpl.Cols, grayTpl.Rows);
      return Task.FromResult(new TemplateMatchResult(new[] { new Vision.TemplateMatch(bbox, maxVal) }, false));
    }
  }
}

internal static partial class ImageMatchLog
{
  [LoggerMessage(Level = Microsoft.Extensions.Logging.LogLevel.Warning, Message = "Slow image match for '{ReferenceImageId}': {ElapsedMs}ms")]
  public static partial void SlowMatch(ILogger logger, string referenceImageId, long elapsedMs, Exception? ex);
}
