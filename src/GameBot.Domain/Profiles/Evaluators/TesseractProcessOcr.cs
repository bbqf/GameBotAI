using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.Runtime.Versioning;
using System.Text;

namespace GameBot.Domain.Profiles.Evaluators;

/// <summary>
/// ITextOcr implementation that shells out to tesseract.exe and parses TSV output for text and confidence.
/// Requires Tesseract installed and available either on PATH or via GAMEBOT_TESSERACT_PATH.
/// Optional: GAMEBOT_TESSERACT_LANG (default "eng"), TESSDATA_PREFIX to locate traineddata.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class TesseractProcessOcr : ITextOcr
{
    private readonly string _exe;
    private readonly string _lang;

    public TesseractProcessOcr()
    {
        _exe = Environment.GetEnvironmentVariable("GAMEBOT_TESSERACT_PATH")?.Trim('"')
               ?? "tesseract";
        _lang = string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("GAMEBOT_TESSERACT_LANG"))
            ? "eng"
            : Environment.GetEnvironmentVariable("GAMEBOT_TESSERACT_LANG")!;
    }

    public OcrResult Recognize(Bitmap image)
    {
        ArgumentNullException.ThrowIfNull(image);

        // Write image to a temp file; tesseract doesn't accept stdin for images.
        var tmp = Path.Combine(Path.GetTempPath(), $"gamebot_ocr_{Guid.NewGuid():N}.png");
        try
        {
            image.Save(tmp, System.Drawing.Imaging.ImageFormat.Png);

            // Request TSV to capture confidences; output to stdout
            // tesseract input.png stdout -l eng tsv
            var psi = new ProcessStartInfo
            {
                FileName = _exe,
                Arguments = $"\"{tmp}\" stdout -l {_lang} tsv",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            using var proc = Process.Start(psi);
            if (proc is null)
            {
                return new OcrResult(string.Empty, 0.0);
            }
            var output = proc.StandardOutput.ReadToEnd();
            var err = proc.StandardError.ReadToEnd();
            proc.WaitForExit(10000);
            if (proc.HasExited == false)
            {
                try { proc.Kill(entireProcessTree: true); } catch { }
                return new OcrResult(string.Empty, 0.0);
            }
            if (proc.ExitCode != 0)
            {
                // Fall back to empty if OCR failed
                return new OcrResult(string.Empty, 0.0);
            }

            // Parse TSV: headers include columns like level, page_num, block_num, par_num, line_num, word_num, left, top, width, height, conf, text
            // We'll collect non-empty text and average positive confidences
            using var reader = new StringReader(output);
            string? line = reader.ReadLine(); // header
            int textIdx = -1, confIdx = -1;
            if (line is not null)
            {
                var headers = line.Split('\t');
                for (int i = 0; i < headers.Length; i++)
                {
                    if (string.Equals(headers[i], "text", StringComparison.OrdinalIgnoreCase)) textIdx = i;
                    if (string.Equals(headers[i], "conf", StringComparison.OrdinalIgnoreCase)) confIdx = i;
                }
            }
            var sb = new StringBuilder();
            double confSum = 0; int confCount = 0;
            while ((line = reader.ReadLine()) is not null)
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                var cols = line.Split('\t');
                if (textIdx >= 0 && textIdx < cols.Length)
                {
                    var t = cols[textIdx];
                    if (!string.IsNullOrWhiteSpace(t))
                    {
                        if (sb.Length > 0) sb.Append(' ');
                        sb.Append(t);
                    }
                }
                if (confIdx >= 0 && confIdx < cols.Length)
                {
                    if (double.TryParse(cols[confIdx], NumberStyles.Any, CultureInfo.InvariantCulture, out var c) && c >= 0)
                    {
                        confSum += c; confCount++;
                    }
                }
            }

            var text = sb.ToString().Trim();
            var avgConf = confCount > 0 ? confSum / confCount : 0.0;
            // Tesseract confidence scale is 0..100; normalize to 0..1
            var norm = Math.Clamp(avgConf / 100.0, 0.0, 1.0);
            return new OcrResult(text, norm);
        }
        catch
        {
            return new OcrResult(string.Empty, 0.0);
        }
        finally
        {
            try { if (File.Exists(tmp)) File.Delete(tmp); } catch { }
        }
    }
}
