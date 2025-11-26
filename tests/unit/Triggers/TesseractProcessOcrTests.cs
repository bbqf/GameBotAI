using System;
using System.Linq;
using System.Drawing;
using System.IO;
using Xunit;
using GameBot.Domain.Triggers.Evaluators;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace GameBot.Tests.Unit.Triggers {
  public class TesseractProcessOcrTests {
    // Stub for TesseractProcessOcr that overrides Recognize to avoid process invocation
    private sealed class StubOcrProcessRunner : ITestOcrProcessRunner {
      private readonly Func<Bitmap, string, string, string?, string?, ILogger, OcrResult> _run;
      public StubOcrProcessRunner(Func<Bitmap, string, string, string?, string?, ILogger, OcrResult> run) { _run = run; }
      public OcrResult Run(Bitmap image, string exePath, string lang, string? psm, string? oem, ILogger logger) => _run(image, exePath, lang, psm, oem, logger);
    }

    [Fact]
    public void RecognizeReturnsExpectedTextAndConfidence() {
      var runner = new StubOcrProcessRunner((img, exe, lang, psm, oem, logger) => new OcrResult("abc123", 1.0));
      using var bmp = new Bitmap(8, 8);
      var ocr = new TesseractProcessOcr("tesseract", "eng", null, null, runner);
      var result = ocr.Recognize(bmp, "eng");
      Assert.Equal("abc123", result.Text);
      Assert.Equal(1.0, result.Confidence);
    }

    [Fact]
    public void RecognizeReturnsZeroConfidenceForEmptyText() {
      var runner = new StubOcrProcessRunner((img, exe, lang, psm, oem, logger) => new OcrResult(string.Empty, 0));
      using var bmp = new Bitmap(8, 8);
      var ocr = new TesseractProcessOcr("tesseract", "eng", null, null, runner);
      var result = ocr.Recognize(bmp, "eng");
      Assert.Equal(0, result.Confidence);
    }

    [Fact]
    public void RecognizeHandlesExceptionAndReturnsEmpty() {
      var runner = new StubOcrProcessRunner((img, exe, lang, psm, oem, logger) => throw new InvalidOperationException("fail"));
      using var bmp = new Bitmap(8, 8);
      var ocr = new TesseractProcessOcr("tesseract", "eng", null, null, runner);
      var result = ocr.Recognize(bmp, "eng");
      Assert.Equal(string.Empty, result.Text);
      Assert.Equal(0, result.Confidence);
    }

    [Fact]
    public void ConstructorUsesEnvironmentVariablesWhenNoArgsProvided() {
      Environment.SetEnvironmentVariable("GAMEBOT_TESSERACT_PATH", "custom_tesseract");
      Environment.SetEnvironmentVariable("GAMEBOT_TESSERACT_LANG", "fra");
      Environment.SetEnvironmentVariable("GAMEBOT_TESSERACT_PSM", "3");
      Environment.SetEnvironmentVariable("GAMEBOT_TESSERACT_OEM", "2");
      var ocr = new TesseractProcessOcr(new NullLogger<TesseractProcessOcr>(), null);
      Assert.Equal("custom_tesseract", GetPrivateField<string>(ocr, "_exePath"));
      Assert.Equal("fra", GetPrivateField<string>(ocr, "_lang"));
      Assert.Equal("3", GetPrivateField<string>(ocr, "_psm"));
      Assert.Equal("2", GetPrivateField<string>(ocr, "_oem"));
    }

    [Fact]
    public void BuildArgsConstructsExpectedArguments() {
      var runner = new StubOcrProcessRunner((img, exe, lang, psm, oem, logger) => new OcrResult("", 0));
      var ocr = new TesseractProcessOcr("tesseract", "eng", "4", "1", runner);
      var method = typeof(TesseractProcessOcr).GetMethod("BuildArgs", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
      Assert.NotNull(method);
      var args = method.Invoke(ocr, new object[] { "input.png", "output", "eng" }) as System.Collections.Generic.IReadOnlyList<string>;
      Assert.NotNull(args);
        Assert.Collection(args!,
          item => Assert.Equal("input.png", item),
          item => Assert.Equal("output", item),
          item => Assert.Equal("-l", item),
          item => Assert.Equal("eng", item),
          item => Assert.Equal("--psm", item),
          item => Assert.Equal("4", item),
          item => Assert.Equal("--oem", item),
          item => Assert.Equal("1", item),
          item => Assert.Equal("-c", item),
          item => Assert.Equal("tessedit_create_tsv=1", item));
    }

    [Fact]
    public void BuildArgsUsesDefaultsWhenValuesAreNullOrWhitespace() {
      var runner = new StubOcrProcessRunner((img, exe, lang, psm, oem, logger) => new OcrResult("", 0));
      var ocr = new TesseractProcessOcr("tesseract", "eng", null, null, runner);
      var method = typeof(TesseractProcessOcr).GetMethod("BuildArgs", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
      Assert.NotNull(method);
      var args = method.Invoke(ocr, new object[] { "input.png", "output", "eng" }) as System.Collections.Generic.IReadOnlyList<string>;
      Assert.NotNull(args);
      var argsArray = args!.ToArray();
      Assert.Equal("6", argsArray[Array.IndexOf(argsArray, "--psm") + 1]);
      Assert.Equal("1", argsArray[Array.IndexOf(argsArray, "--oem") + 1]);
    }

    [Fact]
    public void TryDeleteDeletesFileIfExists() {
      var tmp = Path.GetTempFileName();
      File.WriteAllText(tmp, "test");
      var method = typeof(TesseractProcessOcr).GetMethod("TryDelete", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
      Assert.NotNull(method);
      method.Invoke(null, new object[] { tmp });
      Assert.False(File.Exists(tmp));
    }

    [Fact]
    public void RecognizeReturnsEmptyOnProcessError() {
      // This test simulates the fallback path (no runner) and forces an error
      var ocr = new TesseractProcessOcr("notarealexe", "eng", null, null, null);
      using var bmp = new Bitmap(8, 8);
      var result = ocr.Recognize(bmp, "eng");
      Assert.Equal(string.Empty, result.Text);
      Assert.Equal(0, result.Confidence);
    }

    private static T? GetPrivateField<T>(object obj, string fieldName) {
      var field = obj.GetType().GetField(fieldName, System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
      Assert.NotNull(field);
      return (T?)field.GetValue(obj);
    }
  }
}
