using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.Versioning;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace GameBot.Domain.Profiles.Evaluators;

public interface ITextOcr
{
    OcrResult Recognize(Bitmap image);
    OcrResult Recognize(Bitmap image, string? language);
}

public readonly record struct OcrResult(string Text, double Confidence);

/// <summary>
/// Evaluates text-match triggers using OCR over a screen region.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class TextMatchEvaluator : ITriggerEvaluator
{
    private readonly ITextOcr _ocr;
    private readonly IScreenSource _screen;
    private readonly ILogger<TextMatchEvaluator> _logger;
    public TextMatchEvaluator(ITextOcr ocr, IScreenSource screen, ILogger<TextMatchEvaluator> logger)
    { _ocr = ocr; _screen = screen; _logger = logger; }
    public TextMatchEvaluator(ITextOcr ocr, IScreenSource screen)
        : this(ocr, screen, NullLogger<TextMatchEvaluator>.Instance) { }

    public bool CanEvaluate(ProfileTrigger trigger)
    {
        ArgumentNullException.ThrowIfNull(trigger);
        return trigger.Enabled && trigger.Type == TriggerType.TextMatch;
    }

    public TriggerEvaluationResult Evaluate(ProfileTrigger trigger, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(trigger);
        if (trigger.Params is not TextMatchParams p)
        {
            return new TriggerEvaluationResult
            {
                Status = TriggerStatus.Disabled,
                EvaluatedAt = now,
                Reason = "params_type_mismatch"
            };
        }
        Log.Evaluating(_logger, p.Target ?? string.Empty, p.ConfidenceThreshold, p.Language ?? "(default)");
        using var cropped = CropRegionWithLog(_screen.GetLatestScreenshot(), p.Region);
        if (cropped is null)
        {
            Log.NoScreen(_logger);
            return new TriggerEvaluationResult
            {
                Status = TriggerStatus.Pending,
                EvaluatedAt = now,
                Reason = "no_screen"
            };
        }

        // Log cropped image dimensions
        Log.CroppedSize(_logger, cropped.Width, cropped.Height);

        // Preprocess for small/outlined/aliased text: upscale and binarize
        using var preprocessed = PreprocessForOcr(cropped);
        // Log preprocessing outcome stats (dimensions and approximate white pixel ratio)
        var whiteRatio = EstimateWhiteRatio(preprocessed);
        Log.PreprocessOutcome(_logger, preprocessed.Width, preprocessed.Height, whiteRatio);
        var lang = p.Language;
        var res = lang is not null ? _ocr.Recognize(preprocessed, lang) : _ocr.Recognize(preprocessed);

        bool contains = !string.IsNullOrEmpty(p.Target)
            && res.Text?.IndexOf(p.Target, StringComparison.OrdinalIgnoreCase) >= 0;
        bool confident = res.Confidence >= p.ConfidenceThreshold;
        bool isFoundSatisfied = contains && confident;

        var status = p.Mode.Equals("not-found", StringComparison.OrdinalIgnoreCase)
            ? (isFoundSatisfied ? TriggerStatus.Pending : TriggerStatus.Satisfied)
            : (isFoundSatisfied ? TriggerStatus.Satisfied : TriggerStatus.Pending);

        Log.OcrOutcome(_logger, res.Text?.Length ?? 0, res.Confidence, contains, status.ToString());
        return new TriggerEvaluationResult
        {
            Status = status,
            EvaluatedAt = now,
            Reason = p.Mode.Equals("not-found", StringComparison.OrdinalIgnoreCase)
                        ? (contains ? "text_present" : "text_absent")
                        : (contains ? "text_found" : "text_not_found"),
            Similarity = res.Confidence
        };
    }

    private Bitmap? CropRegionWithLog(Bitmap? screenBmp, Region region)
    {
        if (screenBmp is null) return null;
        // Log screen size and normalized region first
        Log.ScreenSize(_logger, screenBmp.Width, screenBmp.Height);
        Log.NormalizedRegion(_logger, region.X, region.Y, region.Width, region.Height);

        // Pre-rounding pixel coordinates/sizes (double)
        var rxD = region.X * screenBmp.Width;
        var ryD = region.Y * screenBmp.Height;
        var rwD = region.Width * screenBmp.Width;
        var rhD = region.Height * screenBmp.Height;
        Log.PixelMappingPreClamp(_logger, rxD, ryD, rwD, rhD);

        // Rounded integers
        var rx = (int)Math.Round(rxD);
        var ry = (int)Math.Round(ryD);
        var rw = Math.Max(1, (int)Math.Round(rwD));
        var rh = Math.Max(1, (int)Math.Round(rhD));

        // Signal when region collapses by size
        if (rw <= 1 || rh <= 1)
        {
            Log.RegionSmall(_logger, rw, rh);
        }

        // Clamp to screen bounds
        var clampedRx = Math.Clamp(rx, 0, Math.Max(0, screenBmp.Width - 1));
        var clampedRy = Math.Clamp(ry, 0, Math.Max(0, screenBmp.Height - 1));
        var clampedRw = Math.Clamp(rw, 1, screenBmp.Width - clampedRx);
        var clampedRh = Math.Clamp(rh, 1, screenBmp.Height - clampedRy);

        // Log adjustments if any
        if (clampedRx != rx || clampedRy != ry)
        {
            Log.RegionPositionClamped(_logger, rx, ry, clampedRx, clampedRy);
        }
        if (clampedRw != rw || clampedRh != rh)
        {
            Log.RegionSizeClamped(_logger, rw, rh, clampedRw, clampedRh);
        }

        rx = clampedRx; ry = clampedRy; rw = clampedRw; rh = clampedRh;
        Log.RegionMapped(_logger, screenBmp.Width, screenBmp.Height, rx, ry, rw, rh);
        try
        {
            var dest = new Bitmap(rw, rh, PixelFormat.Format24bppRgb);
            using (var g = Graphics.FromImage(dest))
            {
                g.DrawImage(
                    screenBmp,
                    new Rectangle(0, 0, rw, rh),
                    new Rectangle(rx, ry, rw, rh),
                    GraphicsUnit.Pixel);
            }
            return dest;
        }
        catch (Exception ex)
        {
            Log.CropFailed(_logger, ex);
            return null;
        }
        finally
        {
            screenBmp.Dispose();
        }
    }

    /// <summary>
    /// Preprocess image for better OCR quality on small/outlined/aliased text.
    /// Upscales to minimum height and applies binary threshold.
    /// </summary>
    private static Bitmap PreprocessForOcr(Bitmap source)
    {
        const int MinHeight = 32; // Ensure text is at least this tall
        const double ScaleFactor = 2.0; // Default upscale

        // Calculate scale needed
        double scale = source.Height < MinHeight
            ? Math.Max(ScaleFactor, (double)MinHeight / source.Height)
            : ScaleFactor;

        int newWidth = (int)(source.Width * scale);
        int newHeight = (int)(source.Height * scale);

        // Upscale with nearest-neighbor for crisp edges
        var upscaled = new Bitmap(newWidth, newHeight, PixelFormat.Format24bppRgb);
        using (var g = Graphics.FromImage(upscaled))
        {
            g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor;
            g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.Half;
            g.DrawImage(source, 0, 0, newWidth, newHeight);
        }

        // Apply binary threshold to remove anti-aliasing and outlines
        var binarized = new Bitmap(newWidth, newHeight, PixelFormat.Format24bppRgb);
        for (int y = 0; y < newHeight; y++)
        {
            for (int x = 0; x < newWidth; x++)
            {
                var px = upscaled.GetPixel(x, y);
                // Convert to grayscale and threshold
                int gray = (int)(0.299 * px.R + 0.587 * px.G + 0.114 * px.B);
                var bw = gray > 127 ? Color.White : Color.Black;
                binarized.SetPixel(x, y, bw);
            }
        }

        upscaled.Dispose();
        return binarized;
    }

    private static double EstimateWhiteRatio(Bitmap bmp)
    {
        int w = bmp.Width, h = bmp.Height;
        if (w == 0 || h == 0) return 0.0;
        int step = Math.Max(1, Math.Max(w, h) / 64); // sample up to ~64x64 points
        long total = 0, white = 0;
        for (int y = 0; y < h; y += step)
        {
            for (int x = 0; x < w; x += step)
            {
                var px = bmp.GetPixel(x, y);
                // since binarized, white is near 255; threshold is safe
                if (px.R > 200 && px.G > 200 && px.B > 200) white++;
                total++;
            }
        }
        return total > 0 ? (double)white / total : 0.0;
    }
}

/// <summary>
/// Environment-driven OCR implementation for tests/CI without native dependencies.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class EnvTextOcr : ITextOcr
{
    public OcrResult Recognize(Bitmap image)
    {
        var text = Environment.GetEnvironmentVariable("GAMEBOT_TEST_OCR_TEXT") ?? string.Empty;
        var confStr = Environment.GetEnvironmentVariable("GAMEBOT_TEST_OCR_CONF");
        return new OcrResult(text, double.TryParse(confStr, out var c) ? c : (string.IsNullOrEmpty(text) ? 0.0 : 0.99));
    }

    public OcrResult Recognize(Bitmap image, string? language) => Recognize(image);
}

internal static class Log
{
    private static readonly Action<ILogger, string, double, string, Exception?> _evaluating =
        LoggerMessage.Define<string, double, string>(LogLevel.Debug, new EventId(4101, nameof(Evaluating)),
            "TextMatch evaluating target='{Target}' threshold={Threshold} lang='{Language}'");
    private static readonly Action<ILogger, int, int, int, int, int, int, Exception?> _regionMapped =
        LoggerMessage.Define<int, int, int, int, int, int>(LogLevel.Debug, new EventId(4102, nameof(RegionMapped)),
            "OCR region mapped screen({W}x{H}) -> rect x={X} y={Y} w={W2} h={H2}");
    private static readonly Action<ILogger, int, int, Exception?> _screenSize =
        LoggerMessage.Define<int, int>(LogLevel.Debug, new EventId(4108, nameof(ScreenSize)),
            "Screen size {W}x{H}");
    private static readonly Action<ILogger, double, double, double, double, Exception?> _normalizedRegion =
        LoggerMessage.Define<double, double, double, double>(LogLevel.Debug, new EventId(4109, nameof(NormalizedRegion)),
            "Normalized region x={X} y={Y} w={W} h={H}");
    private static readonly Action<ILogger, double, double, double, double, Exception?> _pixelMappingPreClamp =
        LoggerMessage.Define<double, double, double, double>(LogLevel.Debug, new EventId(4110, nameof(PixelMappingPreClamp)),
            "Pixel mapping pre-clamp rx={RX} ry={RY} rw={RW} rh={RH}");
    private static readonly Action<ILogger, int, int, Exception?> _regionSmall =
        LoggerMessage.Define<int, int>(LogLevel.Debug, new EventId(4111, nameof(RegionSmall)),
            "Region too small after rounding rw={RW} rh={RH}");
    private static readonly Action<ILogger, int, int, int, int, Exception?> _regionPositionClamped =
        LoggerMessage.Define<int, int, int, int>(LogLevel.Debug, new EventId(4112, nameof(RegionPositionClamped)),
            "Region position clamped from ({RX},{RY}) to ({CRX},{CRY})");
    private static readonly Action<ILogger, int, int, int, int, Exception?> _regionSizeClamped =
        LoggerMessage.Define<int, int, int, int>(LogLevel.Debug, new EventId(4113, nameof(RegionSizeClamped)),
            "Region size clamped from ({RW},{RH}) to ({CRW},{CRH})");
    private static readonly Action<ILogger, int, int, Exception?> _croppedSize =
        LoggerMessage.Define<int, int>(LogLevel.Debug, new EventId(4106, nameof(CroppedSize)),
            "Cropped image size {W}x{H}");
    private static readonly Action<ILogger, Exception?> _noScreen =
        LoggerMessage.Define(LogLevel.Debug, new EventId(4103, nameof(NoScreen)), "No screen available for OCR");
    private static readonly Action<ILogger, int, double, bool, string, Exception?> _ocrOutcome =
        LoggerMessage.Define<int, double, bool, string>(LogLevel.Debug, new EventId(4104, nameof(OcrOutcome)),
            "OCR result len={Len} conf={Conf} contains={Contains} -> status={Status}");
    private static readonly Action<ILogger, string, Exception?> _cropFailed =
        LoggerMessage.Define<string>(LogLevel.Warning, new EventId(4105, nameof(CropFailed)),
            "Failed to crop region for OCR: {Message}");
    private static readonly Action<ILogger, int, int, double, Exception?> _preprocessOutcome =
        LoggerMessage.Define<int, int, double>(LogLevel.Debug, new EventId(4107, nameof(PreprocessOutcome)),
            "Preprocess result size {W}x{H} whiteRatio={WhiteRatio}");

    public static void Evaluating(ILogger l, string target, double threshold, string language) => _evaluating(l, target, threshold, language, null);
    public static void RegionMapped(ILogger l, int w, int h, int x, int y, int w2, int h2) => _regionMapped(l, w, h, x, y, w2, h2, null);
    public static void ScreenSize(ILogger l, int w, int h) => _screenSize(l, w, h, null);
    public static void NormalizedRegion(ILogger l, double x, double y, double w, double h) => _normalizedRegion(l, x, y, w, h, null);
    public static void PixelMappingPreClamp(ILogger l, double rx, double ry, double rw, double rh) => _pixelMappingPreClamp(l, rx, ry, rw, rh, null);
    public static void RegionSmall(ILogger l, int rw, int rh) => _regionSmall(l, rw, rh, null);
    public static void RegionPositionClamped(ILogger l, int rx, int ry, int crx, int cry) => _regionPositionClamped(l, rx, ry, crx, cry, null);
    public static void RegionSizeClamped(ILogger l, int rw, int rh, int crw, int crh) => _regionSizeClamped(l, rw, rh, crw, crh, null);
    public static void CroppedSize(ILogger l, int w, int h) => _croppedSize(l, w, h, null);
    public static void NoScreen(ILogger l) => _noScreen(l, null);
    public static void OcrOutcome(ILogger l, int len, double conf, bool contains, string status) => _ocrOutcome(l, len, conf, contains, status, null);
    public static void CropFailed(ILogger l, Exception ex) => _cropFailed(l, ex.Message, ex);
    public static void PreprocessOutcome(ILogger l, int w, int h, double whiteRatio) => _preprocessOutcome(l, w, h, whiteRatio, null);
}
