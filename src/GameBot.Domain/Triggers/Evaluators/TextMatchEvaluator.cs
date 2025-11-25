using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.Versioning;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace GameBot.Domain.Triggers.Evaluators;

public interface ITextOcr {
  OcrResult Recognize(System.Drawing.Bitmap image);
  OcrResult Recognize(System.Drawing.Bitmap image, string? language);
}

public readonly record struct OcrResult(string Text, double Confidence);

[SupportedOSPlatform("windows")]
public sealed class TextMatchEvaluator : ITriggerEvaluator {
  private readonly ITextOcr _ocr;
  private readonly IScreenSource _screen;
  private readonly ILogger<TextMatchEvaluator> _logger;
  public TextMatchEvaluator(ITextOcr ocr, IScreenSource screen, ILogger<TextMatchEvaluator> logger) { _ocr = ocr; _screen = screen; _logger = logger; }
  public TextMatchEvaluator(ITextOcr ocr, IScreenSource screen)
      : this(ocr, screen, NullLogger<TextMatchEvaluator>.Instance) { }

  public bool CanEvaluate(Trigger trigger) {
    ArgumentNullException.ThrowIfNull(trigger);
    return trigger.Enabled && trigger.Type == TriggerType.TextMatch;
  }

  public TriggerEvaluationResult Evaluate(Trigger trigger, DateTimeOffset now) {
    ArgumentNullException.ThrowIfNull(trigger);
    if (trigger.Params is not TextMatchParams p) {
      return new TriggerEvaluationResult {
        Status = TriggerStatus.Disabled,
        EvaluatedAt = now,
        Reason = "params_type_mismatch"
      };
    }
    using var cropped = CropRegion(_screen.GetLatestScreenshot(), p.Region);
    if (cropped is null) {
      return new TriggerEvaluationResult {
        Status = TriggerStatus.Pending,
        EvaluatedAt = now,
        Reason = "no_screen"
      };
    }
    using var preprocessed = PreprocessForOcr(cropped);
    var res = p.Language is not null ? _ocr.Recognize(preprocessed, p.Language) : _ocr.Recognize(preprocessed);
    bool contains = !string.IsNullOrEmpty(p.Target) && res.Text?.IndexOf(p.Target, StringComparison.OrdinalIgnoreCase) >= 0;
    bool confident = res.Confidence >= p.ConfidenceThreshold;
    bool isFoundSatisfied = contains && confident;
    var status = p.Mode.Equals("not-found", StringComparison.OrdinalIgnoreCase)
      ? (isFoundSatisfied ? TriggerStatus.Pending : TriggerStatus.Satisfied)
      : (isFoundSatisfied ? TriggerStatus.Satisfied : TriggerStatus.Pending);
    string reason;
    if (p.Mode.Equals("not-found", StringComparison.OrdinalIgnoreCase)) {
      reason = contains ? "text_present" : "text_absent";
    }
    else {
      reason = (contains && confident) ? "text_found" : "text_not_found";
    }
    return new TriggerEvaluationResult {
      Status = status,
      EvaluatedAt = now,
      Reason = reason,
      Similarity = res.Confidence
    };
  }

  private static System.Drawing.Bitmap? CropRegion(System.Drawing.Bitmap? screenBmp, Region region) {
    if (screenBmp is null) return null;
    bool isNormalized = region.Width > 0 && region.Height > 0 && region.X >= 0 && region.Y >= 0 &&
                        region.X <= 1 && region.Y <= 1 && region.Width <= 1 && region.Height <= 1;
    int rx, ry, rw, rh;
    if (isNormalized) {
      rx = (int)Math.Floor(region.X * screenBmp.Width);
      ry = (int)Math.Floor(region.Y * screenBmp.Height);
      rw = (int)Math.Ceiling(region.Width * screenBmp.Width);
      rh = (int)Math.Ceiling(region.Height * screenBmp.Height);
      if (rx < 0) rx = 0; if (ry < 0) ry = 0;
      if (rw < 1) rw = 1; if (rh < 1) rh = 1;
      if (rx + rw > screenBmp.Width) rw = screenBmp.Width - rx;
      if (ry + rh > screenBmp.Height) rh = screenBmp.Height - ry;
    }
    else {
      rx = (int)Math.Clamp(region.X, 0, Math.Max(0, screenBmp.Width - 1));
      ry = (int)Math.Clamp(region.Y, 0, Math.Max(0, screenBmp.Height - 1));
      rw = (int)Math.Clamp(region.Width, 1, screenBmp.Width - rx);
      rh = (int)Math.Clamp(region.Height, 1, screenBmp.Height - ry);
    }
    try {
      var dest = new System.Drawing.Bitmap(rw, rh, PixelFormat.Format24bppRgb);
      using (var g = System.Drawing.Graphics.FromImage(dest)) {
        g.DrawImage(screenBmp, new Rectangle(0, 0, rw, rh), new Rectangle(rx, ry, rw, rh), GraphicsUnit.Pixel);
      }
      return dest;
    }
    catch {
      return null;
    }
    finally { screenBmp.Dispose(); }
  }

  private static System.Drawing.Bitmap PreprocessForOcr(System.Drawing.Bitmap source) {
    const int MinHeight = 32;
    const double ScaleFactor = 2.0;
    double scale = source.Height < MinHeight ? Math.Max(ScaleFactor, (double)MinHeight / source.Height) : ScaleFactor;
    int newWidth = (int)(source.Width * scale);
    int newHeight = (int)(source.Height * scale);
    var upscaled = new System.Drawing.Bitmap(newWidth, newHeight, PixelFormat.Format24bppRgb);
    using (var g = System.Drawing.Graphics.FromImage(upscaled)) {
      g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor;
      g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.Half;
      g.DrawImage(source, 0, 0, newWidth, newHeight);
    }
    var binarized = new System.Drawing.Bitmap(newWidth, newHeight, PixelFormat.Format24bppRgb);
    for (int y = 0; y < newHeight; y++)
      for (int x = 0; x < newWidth; x++) {
        var px = upscaled.GetPixel(x, y);
        int gray = (int)(0.299 * px.R + 0.587 * px.G + 0.114 * px.B);
        var bw = gray > 127 ? System.Drawing.Color.White : System.Drawing.Color.Black;
        binarized.SetPixel(x, y, bw);
      }
    upscaled.Dispose();
    return binarized;
  }
}
