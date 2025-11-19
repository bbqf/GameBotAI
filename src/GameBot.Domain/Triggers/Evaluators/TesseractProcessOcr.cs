using System.Diagnostics;
using System.Drawing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace GameBot.Domain.Triggers.Evaluators;

public sealed class TesseractProcessOcr : ITextOcr
{
    private readonly ILogger<TesseractProcessOcr> _logger;
    private readonly string _exePath;
    private readonly string _lang;
    private readonly string? _psm;
    private readonly string? _oem;
    public TesseractProcessOcr(string exe, string lang, string? psm, string? oem) : this(NullLogger<TesseractProcessOcr>.Instance)
    {
        _exePath = string.IsNullOrWhiteSpace(exe) ? "tesseract" : exe;
        _lang = string.IsNullOrWhiteSpace(lang) ? "eng" : lang;
        _psm = psm;
        _oem = oem;
    }
    public TesseractProcessOcr(ILogger<TesseractProcessOcr> logger)
    {
        _logger = logger;
        _exePath = Environment.GetEnvironmentVariable("GAMEBOT_TESSERACT_PATH") ?? "tesseract";
        _lang = Environment.GetEnvironmentVariable("GAMEBOT_TESSERACT_LANG") ?? "eng";
        _psm = Environment.GetEnvironmentVariable("GAMEBOT_TESSERACT_PSM");
        _oem = Environment.GetEnvironmentVariable("GAMEBOT_TESSERACT_OEM");
    }

    public OcrResult Recognize(Bitmap image) => Recognize(image, _lang);
    public OcrResult Recognize(Bitmap image, string? language)
    {
        ArgumentNullException.ThrowIfNull(image);
        var tmpDir = Path.Combine(Path.GetTempPath(), "gamebot_ocr");
        Directory.CreateDirectory(tmpDir);
        var inputPath = Path.Combine(tmpDir, Guid.NewGuid().ToString("N") + ".png");
        var outputPath = Path.Combine(tmpDir, Guid.NewGuid().ToString("N"));
        image.Save(inputPath);
        var langToUse = language ?? _lang;
        var psi = new ProcessStartInfo
        {
            FileName = _exePath,
            Arguments = BuildArgs(inputPath, outputPath, langToUse),
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        try
        {
            using var p = Process.Start(psi);
            if (p is null) return new OcrResult(string.Empty, 0);
            p.WaitForExit(5000);
            var txtFile = outputPath + ".txt";
            if (!File.Exists(txtFile)) return new OcrResult(string.Empty, 0);
            var text = File.ReadAllText(txtFile);
            double alphaNum = text.Count(c => char.IsLetterOrDigit(c));
            double conf = text.Length == 0 ? 0 : alphaNum / text.Length;
            return new OcrResult(text, conf);
        }
        catch (Exception ex)
        {
            TesseractProcessOcrLog.Failed(_logger, ex);
            return new OcrResult(string.Empty, 0);
        }
        finally
        {
            TryDelete(inputPath);
            TryDelete(outputPath + ".txt");
        }
    }

    private string BuildArgs(string inputPath, string outputPath, string lang)
    {
        var parts = new List<string> { $"\"{inputPath}\"", $"\"{outputPath}\"" };
        if (!string.IsNullOrWhiteSpace(lang)) parts.Add($"-l {lang}");
        if (!string.IsNullOrWhiteSpace(_psm)) parts.Add($"--psm {_psm}");
        if (!string.IsNullOrWhiteSpace(_oem)) parts.Add($"--oem {_oem}"); else parts.Add("--oem 1");
        if (string.IsNullOrWhiteSpace(_psm)) parts.Add("--psm 6");
        return string.Join(' ', parts);
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }
}

internal static partial class TesseractProcessOcrLog
{
    [LoggerMessage(EventId = 5001, Level = LogLevel.Warning, Message = "tesseract_failed")]
    public static partial void Failed(ILogger logger, Exception ex);
}