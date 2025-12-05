using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System.Runtime.Versioning;

namespace GameBot.Domain.Triggers.Evaluators;

public interface ITestOcrProcessRunner {
  OcrResult Run(Bitmap image, string exePath, string lang, string? psm, string? oem, ILogger logger);
}

public interface ITesseractInvocationLogger {
  void Log(in TesseractInvocationContext context);
}

public readonly record struct TesseractInvocationCapture(string Content, bool WasTruncated);

public readonly record struct TesseractInvocationContext(
  Guid InvocationId,
  string ExePath,
  IReadOnlyList<string> Arguments,
  string? WorkingDirectory,
  IReadOnlyDictionary<string, string?> EnvironmentOverrides,
  DateTimeOffset StartedAtUtc,
  DateTimeOffset CompletedAtUtc,
  int? ExitCode,
  TesseractInvocationCapture StdOut,
  TesseractInvocationCapture StdErr);

[SupportedOSPlatform("windows")]
public sealed class TesseractProcessOcr : ITextOcr {
  private readonly ILogger<TesseractProcessOcr> _logger;
  private readonly string _exePath;
  private readonly string _lang;
  private readonly string? _psm;
  private readonly string? _oem;
  private readonly ITestOcrProcessRunner? _runner;
  private readonly ITesseractInvocationLogger? _invocationLogger;
  private static readonly IReadOnlyDictionary<string, string?> EmptyEnvOverrides = new ReadOnlyDictionary<string, string?>(new Dictionary<string, string?>());
  private const int StreamCaptureLimit = 8 * 1024;

  public TesseractProcessOcr(string exe, string lang, string? psm, string? oem, ITestOcrProcessRunner? runner = null)
    : this(exe, lang, psm, oem, invocationLogger: null, runner) { }

  public TesseractProcessOcr(string exe, string lang, string? psm, string? oem, ITesseractInvocationLogger? invocationLogger, ITestOcrProcessRunner? runner = null)
    : this(NullLogger<TesseractProcessOcr>.Instance, invocationLogger, runner) {
    _exePath = string.IsNullOrWhiteSpace(exe) ? "tesseract" : exe;
    _lang = string.IsNullOrWhiteSpace(lang) ? "eng" : lang;
    _psm = psm;
    _oem = oem;
  }
  public TesseractProcessOcr(ILogger<TesseractProcessOcr> logger, ITesseractInvocationLogger? invocationLogger = null, ITestOcrProcessRunner? runner = null) {
    _logger = logger;
    _invocationLogger = invocationLogger;
    _exePath = Environment.GetEnvironmentVariable("GAMEBOT_TESSERACT_PATH") ?? "tesseract";
    _lang = Environment.GetEnvironmentVariable("GAMEBOT_TESSERACT_LANG") ?? "eng";
    _psm = Environment.GetEnvironmentVariable("GAMEBOT_TESSERACT_PSM");
    _oem = Environment.GetEnvironmentVariable("GAMEBOT_TESSERACT_OEM");
    _runner = runner;
  }

  public OcrResult Recognize(Bitmap image) => Recognize(image, _lang);
  public OcrResult Recognize(Bitmap image, string? language) {
    ArgumentNullException.ThrowIfNull(image);
    if (_runner != null) {
      try {
        return _runner.Run(image, _exePath, language ?? _lang, _psm, _oem, _logger);
      }
      catch (Exception ex) {
        TesseractProcessOcrLog.Failed(_logger, ex);
        return new OcrResult(string.Empty, 0);
      }
    }
    var tmpDir = Path.Combine(Path.GetTempPath(), "gamebot_ocr");
    Directory.CreateDirectory(tmpDir);
    var inputPath = Path.Combine(tmpDir, Guid.NewGuid().ToString("N") + ".png");
    var outputPath = Path.Combine(tmpDir, Guid.NewGuid().ToString("N"));
    image.Save(inputPath);
    var langToUse = language ?? _lang;
    var arguments = BuildArgs(inputPath, outputPath, langToUse);
    var psi = new ProcessStartInfo {
      FileName = _exePath,
      UseShellExecute = false,
      RedirectStandardOutput = true,
      RedirectStandardError = true,
      CreateNoWindow = true,
      WorkingDirectory = tmpDir
    };
    foreach (var arg in arguments) {
      psi.ArgumentList.Add(arg);
    }
    var invocationId = Guid.NewGuid();
    var startedAt = DateTimeOffset.UtcNow;
    try {
      using var p = Process.Start(psi);
      if (p is null) return new OcrResult(string.Empty, 0);
      var stdoutTask = Task.Run(() => CaptureStream(p.StandardOutput));
      var stderrTask = Task.Run(() => CaptureStream(p.StandardError));
      var exited = p.WaitForExit(5000);
      if (!exited) {
        try { p.Kill(entireProcessTree: true); } catch { }
      }
      var completedAt = DateTimeOffset.UtcNow;
      var stdout = stdoutTask.GetAwaiter().GetResult();
      var stderr = stderrTask.GetAwaiter().GetResult();
      var exitCode = p.HasExited ? p.ExitCode : (int?)null;
      _invocationLogger?.Log(new TesseractInvocationContext(
        invocationId,
        psi.FileName,
        arguments.AsReadOnly(),
        psi.WorkingDirectory,
        EmptyEnvOverrides,
        startedAt,
        completedAt,
        exitCode,
        stdout,
        stderr));
      var txtFile = outputPath + ".txt";
      var tsvFile = outputPath + ".tsv";
      string text = string.Empty;
      double confidence = 0;
      if (File.Exists(tsvFile)) {
        try {
          var tsv = File.ReadAllText(tsvFile);
          var tokens = TesseractTsvParser.Parse(tsv, out var agg, out var reason);
          if (File.Exists(txtFile)) {
            text = File.ReadAllText(txtFile);
          }
          else {
            text = BuildTextFromTokens(tokens);
          }
          confidence = (agg > 0) ? agg / 100.0 : ComputeConfidence(text);
          return new OcrResult(text, confidence);
        }
        catch {
          // Fall back to text if TSV parsing fails
          if (File.Exists(txtFile)) {
            text = File.ReadAllText(txtFile);
            confidence = ComputeConfidence(text);
            return new OcrResult(text, confidence);
          }
          return new OcrResult(string.Empty, 0);
        }
      }
      // No TSV file; fall back to TXT only
      if (!File.Exists(txtFile)) return new OcrResult(string.Empty, 0);
      text = File.ReadAllText(txtFile);
      confidence = ComputeConfidence(text);
      return new OcrResult(text, confidence);
    }
    catch (Exception ex) {
      TesseractProcessOcrLog.Failed(_logger, ex);
      return new OcrResult(string.Empty, 0);
    }
    finally {
      TryDelete(inputPath);
      TryDelete(outputPath + ".txt");
      TryDelete(outputPath + ".tsv");
    }
  }

  internal static double ComputeConfidence(string? textOrTsv) {
    if (string.IsNullOrEmpty(textOrTsv)) return 0;
    // If the input looks like TSV (header with columns including 'conf' and 'text'),
    // compute confidence from TSV aggregate (0-100 scaled to 0-1).
    if (textOrTsv.AsSpan().IndexOf('\t') >= 0) {
      var firstLineEnd = textOrTsv.IndexOf('\n', StringComparison.Ordinal);
      var header = firstLineEnd >= 0 ? textOrTsv.Substring(0, firstLineEnd) : textOrTsv;
      if (header.Contains("conf", StringComparison.Ordinal) && header.Contains("text", StringComparison.Ordinal)) {
        try {
          var _ = TesseractTsvParser.Parse(textOrTsv, out var agg, out var reason);
          if (agg > 0) return agg / 100.0;
        } catch { /* fall back to heuristic below */ }
      }
    }
    // Fallback heuristic: alphanumeric ratio for plain text inputs
    var total = textOrTsv.Length;
    if (total == 0) return 0;
    var alphaNum = textOrTsv.Count(char.IsLetterOrDigit);
    if (alphaNum <= 0) return 0;
    var ratio = alphaNum / (double)total;
    if (double.IsNaN(ratio) || double.IsInfinity(ratio) || ratio < 0) return 0;
    return ratio;
  }

  private List<string> BuildArgs(string inputPath, string outputPath, string lang) {
    var parts = new List<string> { inputPath, outputPath };
    if (!string.IsNullOrWhiteSpace(lang)) {
      parts.Add("-l");
      parts.Add(lang);
    }
    if (!string.IsNullOrWhiteSpace(_psm)) {
      parts.Add("--psm");
      parts.Add(_psm!);
    }
    else {
      parts.Add("--psm");
      parts.Add("6");
    }
    if (!string.IsNullOrWhiteSpace(_oem)) {
      parts.Add("--oem");
      parts.Add(_oem!);
    }
    else {
      parts.Add("--oem");
      parts.Add("1");
    }
    // Request TSV output in addition to text file
    parts.Add("-c");
    parts.Add("tessedit_create_tsv=1");
    return parts;
  }

  private static string BuildTextFromTokens(IReadOnlyList<OcrToken> tokens) {
    if (tokens is null || tokens.Count == 0) return string.Empty;
    var lines = tokens
      .GroupBy(t => t.LineIndex)
      .OrderBy(g => g.Key)
      .Select(g => string.Join(" ", g.OrderBy(t => t.WordIndex).Select(t => t.Text)));
    return string.Join("\n", lines);
  }

  private static void TryDelete(string path) {
    try { if (File.Exists(path)) File.Delete(path); } catch { }
  }

  private static TesseractInvocationCapture CaptureStream(StreamReader reader) {
    var builder = new StringBuilder();
    var buffer = new char[512];
    var truncated = false;
    while (true) {
      var read = reader.Read(buffer, 0, buffer.Length);
      if (read <= 0) break;
      if (!truncated) {
        var remaining = StreamCaptureLimit - builder.Length;
        if (remaining > 0) {
          var toCopy = Math.Min(read, remaining);
          builder.Append(buffer, 0, toCopy);
          if (toCopy < read || builder.Length >= StreamCaptureLimit) {
            truncated = true;
          }
        }
        else {
          truncated = true;
        }
      }
      else {
        // Already truncated; continue draining without storing additional data.
      }
    }
    return new TesseractInvocationCapture(builder.ToString(), truncated);
  }
}

internal static partial class TesseractProcessOcrLog {
  [LoggerMessage(EventId = 5001, Level = LogLevel.Warning, Message = "tesseract_failed")]
  public static partial void Failed(ILogger logger, Exception ex);
}
