using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.Versioning;

namespace GameBot.Domain.Profiles.Evaluators;

public interface ITextOcr {
    OcrResult Recognize(Bitmap image);
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
    public TextMatchEvaluator(ITextOcr ocr, IScreenSource screen)
    { _ocr = ocr; _screen = screen; }

    public bool CanEvaluate(ProfileTrigger trigger)
    {
        ArgumentNullException.ThrowIfNull(trigger);
        return trigger.Enabled && trigger.Type == TriggerType.TextMatch;
    }

    public TriggerEvaluationResult Evaluate(ProfileTrigger trigger, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(trigger);
        var p = (TextMatchParams)trigger.Params;
        using var cropped = CropRegion(_screen.GetLatestScreenshot(), p.Region);
        if (cropped is null)
        {
            return new TriggerEvaluationResult
            {
                Status = TriggerStatus.Pending,
                EvaluatedAt = now,
                Reason = "no_screen"
            };
        }
        var res = _ocr.Recognize(cropped);

        bool contains = !string.IsNullOrEmpty(p.Target)
            && res.Text?.IndexOf(p.Target, StringComparison.OrdinalIgnoreCase) >= 0;
        bool confident = res.Confidence >= p.ConfidenceThreshold;
        bool isFoundSatisfied = contains && confident;

        var status = p.Mode.Equals("not-found", StringComparison.OrdinalIgnoreCase)
            ? (isFoundSatisfied ? TriggerStatus.Pending : TriggerStatus.Satisfied)
            : (isFoundSatisfied ? TriggerStatus.Satisfied : TriggerStatus.Pending);

        var result = new TriggerEvaluationResult {
            Status = status,
            EvaluatedAt = now,
            Reason = p.Mode.Equals("not-found", StringComparison.OrdinalIgnoreCase)
                        ? (contains ? "text_present" : "text_absent")
                        : (contains ? "text_found" : "text_not_found"),
            Similarity = res.Confidence
        };

        return result;
    }

    private static Bitmap? CropRegion(Bitmap? screenBmp, Region region)
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
        return screenBmp.Clone(new Rectangle(rx, ry, rw, rh), PixelFormat.Format24bppRgb);
    }
}

/// <summary>
/// Environment-driven OCR implementation for tests/CI without native dependencies.
/// Reads text from GAMEBOT_TEST_OCR_TEXT and confidence from GAMEBOT_TEST_OCR_CONF.
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
}
