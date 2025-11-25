using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Text;
using FluentAssertions;
using GameBot.Domain.Triggers.Evaluators;
using Microsoft.Extensions.Logging;
using Xunit;

namespace GameBot.UnitTests.TextOcr;

public class TesseractProcessOcrTextTests {
  private static readonly MethodInfo BuildArgsMethod = typeof(TesseractProcessOcr)
      .GetMethod("BuildArgs", BindingFlags.NonPublic | BindingFlags.Instance)!;
  private static readonly MethodInfo CaptureStreamMethod = typeof(TesseractProcessOcr)
      .GetMethod("CaptureStream", BindingFlags.NonPublic | BindingFlags.Static)!;
  private static readonly MethodInfo TryDeleteMethod = typeof(TesseractProcessOcr)
      .GetMethod("TryDelete", BindingFlags.NonPublic | BindingFlags.Static)!;

  [Theory]
  [InlineData(null, 0)]
  [InlineData("", 0)]
  [InlineData("\0\0\0", 0)]
  [InlineData("%%%%", 0)]
  public void ComputeConfidenceReturnsZeroForMalformedStrings(string? input, double expected) {
    TesseractProcessOcr.ComputeConfidence(input).Should().Be(expected);
  }

  [Fact]
  public void ComputeConfidenceIgnoresNonAlphanumericNoise() {
    var text = "SCORE: 1234***";
    var result = TesseractProcessOcr.ComputeConfidence(text);
    result.Should().BeGreaterThan(0);
    result.Should().BeLessThan(1);
  }

  [Fact]
  public void RecognizeUsesInjectedRunnerAndOverridesLanguageWhenProvided() {
    using var bitmap = new Bitmap(1, 1);
    var runner = new CapturingRunner(new OcrResult("captured", 0.9));
    var sut = new TesseractProcessOcr("custom.exe", "eng", "7", "2", runner);

    var result = sut.Recognize(bitmap, language: "spa");

    result.Text.Should().Be("captured");
    result.Confidence.Should().Be(0.9);
    runner.CallCount.Should().Be(1);
    runner.LastExePath.Should().Be("custom.exe");
    runner.LastLanguage.Should().Be("spa");
    runner.LastPsm.Should().Be("7");
    runner.LastOem.Should().Be("2");
  }

  [Fact]
  public void RecognizeReturnsEmptyResultWhenRunnerThrows() {
    using var bitmap = new Bitmap(1, 1);
    var sut = new TesseractProcessOcr("missing.exe", "eng", null, null, new ThrowingRunner());

    var result = sut.Recognize(bitmap);

    result.Text.Should().BeEmpty();
    result.Confidence.Should().Be(0);
  }

  [Fact]
  public void BuildArgsFallsBackToDefaultsWhenOptionsMissing() {
    var sut = new TesseractProcessOcr("exe", "eng", null, null, runner: new CapturingRunner(new OcrResult("", 0)));
    var args = InvokeBuildArgs(sut, "input.png", "output", "eng");

    args.Should().ContainInOrder("input.png", "output", "-l", "eng");
    args.Should().Contain("--psm");
    args.Should().Contain("6");
    args.Should().Contain("--oem");
    args.Should().Contain("1");
  }

  [Fact]
  public void BuildArgsPreservesCustomPsmAndOem() {
    var sut = new TesseractProcessOcr("exe", "eng", "4", "3", runner: new CapturingRunner(new OcrResult("", 0)));
    var args = InvokeBuildArgs(sut, "input.png", "output", string.Empty);

    args.Should().Contain("--psm");
    args.Should().Contain("4");
    args.Should().Contain("--oem");
    args.Should().Contain("3");
    args.Should().NotContain("-l");
  }

  [Fact]
  public void CaptureStreamFlagsTruncationOnceLimitReached() {
    var longText = new string('a', 9000);
    var capture = InvokeCaptureStream(longText);

    capture.WasTruncated.Should().BeTrue();
    capture.Content.Length.Should().Be(8 * 1024);
  }

  [Fact]
  public void CaptureStreamReturnsFullContentWhenUnderLimit() {
    var capture = InvokeCaptureStream("hello world");

    capture.WasTruncated.Should().BeFalse();
    capture.Content.Should().Be("hello world");
  }

  [Fact]
  public void TryDeleteRemovesExistingFilesAndSwallowsMissingOnes() {
    var path = Path.GetTempFileName();
    File.WriteAllText(path, "temp");

    Action act = () => InvokeTryDelete(path);
    act.Should().NotThrow();
    File.Exists(path).Should().BeFalse();

    Action missingAct = () => InvokeTryDelete(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".tmp"));
    missingAct.Should().NotThrow();
  }

  private static List<string> InvokeBuildArgs(TesseractProcessOcr sut, string input, string output, string lang) {
    return (List<string>)BuildArgsMethod.Invoke(sut, new object[] { input, output, lang })!;
  }

  private static TesseractInvocationCapture InvokeCaptureStream(string content) {
    using var stream = new MemoryStream(Encoding.UTF8.GetBytes(content));
    using var reader = new StreamReader(stream, Encoding.UTF8, leaveOpen: true);
    return (TesseractInvocationCapture)CaptureStreamMethod.Invoke(null, new object[] { reader })!;
  }

  private static void InvokeTryDelete(string path) {
    TryDeleteMethod.Invoke(null, new object[] { path });
  }

  private sealed class CapturingRunner : ITestOcrProcessRunner {
    private readonly OcrResult _result;

    public CapturingRunner(OcrResult result) {
      _result = result;
    }

    public string? LastExePath { get; private set; }
    public string? LastLanguage { get; private set; }
    public string? LastPsm { get; private set; }
    public string? LastOem { get; private set; }
    public int CallCount { get; private set; }

    public OcrResult Run(Bitmap image, string exePath, string lang, string? psm, string? oem, ILogger logger) {
      CallCount++;
      LastExePath = exePath;
      LastLanguage = lang;
      LastPsm = psm;
      LastOem = oem;
      return _result;
    }
  }

  private sealed class ThrowingRunner : ITestOcrProcessRunner {
    public OcrResult Run(Bitmap image, string exePath, string lang, string? psm, string? oem, ILogger logger) {
      throw new InvalidOperationException("runner failed");
    }
  }
}
