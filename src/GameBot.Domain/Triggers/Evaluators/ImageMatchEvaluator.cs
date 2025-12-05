using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.Versioning;
using GameBot.Domain.Vision;

namespace GameBot.Domain.Triggers.Evaluators;

[SupportedOSPlatform("windows")]
public sealed class ImageMatchEvaluator : ITriggerEvaluator {
  private readonly IReferenceImageStore _store;
  private readonly IScreenSource _screen;
  public ImageMatchEvaluator(IReferenceImageStore store, IScreenSource screen) { _store = store; _screen = screen; }

  public bool CanEvaluate(Trigger trigger) {
    ArgumentNullException.ThrowIfNull(trigger);
    return trigger.Enabled && trigger.Type == TriggerType.ImageMatch;
  }

  public TriggerEvaluationResult Evaluate(Trigger trigger, DateTimeOffset now) {
    ArgumentNullException.ThrowIfNull(trigger);
    var p = (ImageMatchParams)trigger.Params;
    var similarity = ComputeSimilarityNcc(p);
    var status = similarity >= p.SimilarityThreshold ? TriggerStatus.Satisfied : TriggerStatus.Pending;
    return new TriggerEvaluationResult {
      Status = status,
      Similarity = similarity,
      EvaluatedAt = now,
      Reason = status == TriggerStatus.Satisfied ? "similarity_met" : "similarity_below_threshold"
    };
  }

  private double ComputeSimilarityNcc(ImageMatchParams p) {
    if (!_store.TryGet(p.ReferenceImageId, out var tpl)) return 0d;
    using var screenBmp = _screen.GetLatestScreenshot();
    if (screenBmp is null) return 0d;
    var rx = (int)Math.Round(p.Region.X * screenBmp.Width);
    var ry = (int)Math.Round(p.Region.Y * screenBmp.Height);
    var rw = Math.Max(1, (int)Math.Round(p.Region.Width * screenBmp.Width));
    var rh = Math.Max(1, (int)Math.Round(p.Region.Height * screenBmp.Height));
    rx = Math.Clamp(rx, 0, Math.Max(0, screenBmp.Width - 1));
    ry = Math.Clamp(ry, 0, Math.Max(0, screenBmp.Height - 1));
    rw = Math.Clamp(rw, 1, screenBmp.Width - rx);
    rh = Math.Clamp(rh, 1, screenBmp.Height - ry);
    using var region = new Bitmap(rw, rh, PixelFormat.Format24bppRgb);
    using (var g = Graphics.FromImage(region)) {
      g.DrawImage(screenBmp, new Rectangle(0, 0, rw, rh), new Rectangle(rx, ry, rw, rh), GraphicsUnit.Pixel);
    }
    using var tpl24 = new Bitmap(tpl.Width, tpl.Height, PixelFormat.Format24bppRgb);
    using (var gTpl = Graphics.FromImage(tpl24)) {
      gTpl.DrawImage(tpl, new Rectangle(0, 0, tpl.Width, tpl.Height), new Rectangle(0, 0, tpl.Width, tpl.Height), GraphicsUnit.Pixel);
    }
    if (tpl24.Width > region.Width || tpl24.Height > region.Height) return 0d;
    var regionGray = ImageProcessing.ToGrayscale(region);
    var tplGray = ImageProcessing.ToGrayscale(tpl24);
    if (IsConstant(tplGray, out var tplVal) && IsConstant(regionGray, out var regVal)) {
      return Math.Abs(tplVal - regVal) < 1e-6 ? 1.0 : 0.0;
    }
    double best = double.NegativeInfinity;
    for (int y = 0; y <= regionGray.Height - tplGray.Height; y++)
      for (int x = 0; x <= regionGray.Width - tplGray.Width; x++) {
        var ncc = Ncc(regionGray, tplGray, x, y);
        if (ncc > best) best = ncc;
      }
    return Math.Max(0, Math.Min(1, (best + 1) / 2.0));
  }

  

  private static double Ncc(GrayImage img, GrayImage tpl, int x0, int y0) {
    double sumI = 0, sumT = 0, sumI2 = 0, sumT2 = 0, sumIT = 0;
    int n = tpl.Width * tpl.Height;
    for (int y = 0; y < tpl.Height; y++)
      for (int x = 0; x < tpl.Width; x++) {
        var I = img.Data[(y0 + y) * img.Width + (x0 + x)];
        var T = tpl.Data[y * tpl.Width + x];
        sumI += I; sumT += T; sumI2 += I * I; sumT2 += T * T; sumIT += I * T;
      }
    var num = sumIT - (sumI * sumT / n);
    var denL = sumI2 - (sumI * sumI / n);
    var denR = sumT2 - (sumT * sumT / n);
    var den = Math.Sqrt(Math.Max(denL, 0) * Math.Max(denR, 0));
    if (den == 0) {
      bool constI = denL == 0;
      bool constT = denR == 0;
      if (constI && constT) {
        return 1.0;
      }
      return -1.0;
    }
    return num / den;
  }

  private static bool IsConstant(GrayImage img, out double value) {
    double sum = 0;
    for (int i = 0; i < img.Data.Length; i++) sum += img.Data[i];
    double mean = sum / img.Data.Length;
    double var = 0;
    for (int i = 0; i < img.Data.Length; i++) {
      double d = img.Data[i] - mean;
      var += d * d;
    }
    value = mean;
    return var < 1e-6;
  }
}
