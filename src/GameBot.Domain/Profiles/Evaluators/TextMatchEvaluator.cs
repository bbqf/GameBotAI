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
        var lang = p.Language;
        var res = lang is not null ? _ocr.Recognize(cropped, lang) : _ocr.Recognize(cropped);

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
        var rx = (int)Math.Round(region.X * screenBmp.Width);
        var ry = (int)Math.Round(region.Y * screenBmp.Height);
        var rw = Math.Max(1, (int)Math.Round(region.Width * screenBmp.Width));
        var rh = Math.Max(1, (int)Math.Round(region.Height * screenBmp.Height));
        rx = Math.Clamp(rx, 0, Math.Max(0, screenBmp.Width - 1));
        ry = Math.Clamp(ry, 0, Math.Max(0, screenBmp.Height - 1));
        rw = Math.Clamp(rw, 1, screenBmp.Width - rx);
        rh = Math.Clamp(rh, 1, screenBmp.Height - ry);
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
        LoggerMessage.Define<int, int, int, int, int, int>(LogLevel.Trace, new EventId(4102, nameof(RegionMapped)),
            "OCR region mapped screen({W}x{H}) -> rect x={X} y={Y} w={W2} h={H2}");
    private static readonly Action<ILogger, Exception?> _noScreen =
        LoggerMessage.Define(LogLevel.Debug, new EventId(4103, nameof(NoScreen)), "No screen available for OCR");
    private static readonly Action<ILogger, int, double, bool, string, Exception?> _ocrOutcome =
        LoggerMessage.Define<int, double, bool, string>(LogLevel.Debug, new EventId(4104, nameof(OcrOutcome)),
            "OCR result len={Len} conf={Conf} contains={Contains} -> status={Status}");
    private static readonly Action<ILogger, string, Exception?> _cropFailed =
        LoggerMessage.Define<string>(LogLevel.Warning, new EventId(4105, nameof(CropFailed)),
            "Failed to crop region for OCR: {Message}");

    public static void Evaluating(ILogger l, string target, double threshold, string language) => _evaluating(l, target, threshold, language, null);
    public static void RegionMapped(ILogger l, int w, int h, int x, int y, int w2, int h2) => _regionMapped(l, w, h, x, y, w2, h2, null);
    public static void NoScreen(ILogger l) => _noScreen(l, null);
    public static void OcrOutcome(ILogger l, int len, double conf, bool contains, string status) => _ocrOutcome(l, len, conf, contains, status, null);
    public static void CropFailed(ILogger l, Exception ex) => _cropFailed(l, ex.Message, ex);
}
