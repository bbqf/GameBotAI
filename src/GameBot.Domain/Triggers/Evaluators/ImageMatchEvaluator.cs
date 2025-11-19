using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.Versioning;
using System.Diagnostics.CodeAnalysis;

namespace GameBot.Domain.Triggers.Evaluators;

[SupportedOSPlatform("windows")]
public sealed class ImageMatchEvaluator : ITriggerEvaluator
{
    private readonly IReferenceImageStore _store;
    private readonly IScreenSource _screen;
    public ImageMatchEvaluator(IReferenceImageStore store, IScreenSource screen)
    { _store = store; _screen = screen; }

    public bool CanEvaluate(Trigger trigger)
    {
        ArgumentNullException.ThrowIfNull(trigger);
        return trigger.Enabled && trigger.Type == TriggerType.ImageMatch;
    }

    public TriggerEvaluationResult Evaluate(Trigger trigger, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(trigger);
        var p = (ImageMatchParams)trigger.Params;
        var similarity = ComputeSimilarityNcc(p);
        var status = similarity >= p.SimilarityThreshold ? TriggerStatus.Satisfied : TriggerStatus.Pending;
        return new TriggerEvaluationResult
        {
            Status = status,
            Similarity = similarity,
            EvaluatedAt = now,
            Reason = status == TriggerStatus.Satisfied ? "similarity_met" : "similarity_below_threshold"
        };
    }

    private double ComputeSimilarityNcc(ImageMatchParams p)
    {
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
        using var region = screenBmp.Clone(new Rectangle(rx, ry, rw, rh), PixelFormat.Format24bppRgb);
        using var tpl24 = tpl.PixelFormat == PixelFormat.Format24bppRgb ? (Bitmap)tpl.Clone() : tpl.Clone(new Rectangle(0,0,tpl.Width,tpl.Height), PixelFormat.Format24bppRgb);
        if (tpl24.Width > region.Width || tpl24.Height > region.Height) return 0d;
        var regionGray = ToGrayscale(region);
        var tplGray = ToGrayscale(tpl24);
        if (IsConstant(tplGray, out var tplVal) && IsConstant(regionGray, out var regVal))
        {
            return Math.Abs(tplVal - regVal) < 1e-6 ? 1.0 : 0.0;
        }
        double best = -1;
        for (int y = 0; y <= regionGray.Height - tplGray.Height; y++)
        for (int x = 0; x <= regionGray.Width - tplGray.Width; x++)
        {
            var ncc = Ncc(regionGray, tplGray, x, y);
            if (ncc > best) best = ncc;
        }
        if (best < 0) return 0d;
        return Math.Max(0, Math.Min(1, (best + 1) / 2.0));
    }

    private static GrayImage ToGrayscale(Bitmap bmp)
    {
        var w = bmp.Width; var h = bmp.Height;
        var data = new byte[w * h];
        var bd = bmp.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);
        try
        {
            unsafe
            {
                for (int y = 0; y < h; y++)
                {
                    byte* row = (byte*)bd.Scan0 + y * bd.Stride;
                    for (int x = 0; x < w; x++)
                    {
                        byte b = row[x * 3 + 0];
                        byte g = row[x * 3 + 1];
                        byte r = row[x * 3 + 2];
                        data[y * w + x] = (byte)((r * 0.299) + (g * 0.587) + (b * 0.114));
                    }
                }
            }
        }
        finally
        {
            bmp.UnlockBits(bd);
        }
        return new GrayImage(w, h, data);
    }

    private static double Ncc(GrayImage img, GrayImage tpl, int x0, int y0)
    {
        double sumI = 0, sumT = 0, sumI2 = 0, sumT2 = 0, sumIT = 0;
        int n = tpl.Width * tpl.Height;
        for (int y = 0; y < tpl.Height; y++)
        for (int x = 0; x < tpl.Width; x++)
        {
            var I = img.Data[(y0 + y) * img.Width + (x0 + x)];
            var T = tpl.Data[y * tpl.Width + x];
            sumI += I; sumT += T; sumI2 += I * I; sumT2 += T * T; sumIT += I * T;
        }
        var num = sumIT - (sumI * sumT / n);
        var denL = sumI2 - (sumI * sumI / n);
        var denR = sumT2 - (sumT * sumT / n);
        var den = Math.Sqrt(Math.Max(denL, 0) * Math.Max(denR, 0));
        if (den == 0)
        {
            bool constI = denL == 0;
            bool constT = denR == 0;
            if (constI && constT)
            {
                return 1.0;
            }
            return -1.0;
        }
        return num / den;
    }

    private static bool IsConstant(GrayImage img, out double value)
    {
        double sum = 0;
        for (int i = 0; i < img.Data.Length; i++) sum += img.Data[i];
        double mean = sum / img.Data.Length;
        double var = 0;
        for (int i = 0; i < img.Data.Length; i++)
        {
            double d = img.Data[i] - mean;
            var += d * d;
        }
        value = mean;
        return var < 1e-6;
    }
}

[SuppressMessage("Performance", "CA1819:Properties should not return arrays")]
public sealed class GrayImage
{
    public int Width { get; }
    public int Height { get; }
    public byte[] Data { get; }
    public GrayImage(int w, int h, byte[] data) { Width = w; Height = h; Data = data; }
}