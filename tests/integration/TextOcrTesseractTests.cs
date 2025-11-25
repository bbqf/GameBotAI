using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using FluentAssertions;
using GameBot.Domain.Triggers.Evaluators;
using GameBot.IntegrationTests.Helpers;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace GameBot.IntegrationTests;

public class TextOcrTesseractTests {
  private static bool IsTesseractAvailable() {
    var exe = Environment.GetEnvironmentVariable("GAMEBOT_TESSERACT_PATH");
    if (!string.IsNullOrWhiteSpace(exe) && System.IO.File.Exists(exe)) return true;
    try {
      var psi = new ProcessStartInfo { FileName = "tesseract", Arguments = "--version", UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
      using var p = Process.Start(psi);
      if (p == null) return false;
      p.WaitForExit(2000);
      return p.HasExited && p.ExitCode == 0;
    }
    catch { return false; }
  }

  [Fact]
  public void RecognizesSimpleTextWhenTesseractEnabled() {
    if (!IsTesseractAvailable()) return; // Skip if tesseract not present
    Environment.SetEnvironmentVariable("GAMEBOT_USE_ADB", "false");
    Environment.SetEnvironmentVariable("GAMEBOT_TESSERACT_ENABLED", "true");
    GameBot.IntegrationTests.TestEnvironment.PrepareCleanDataDir();

    using var app = new WebApplicationFactory<Program>();
    using var scope = app.Services.CreateScope();
    var ocr = scope.ServiceProvider.GetRequiredService<GameBot.Domain.Triggers.Evaluators.ITextOcr>();

    using var bmp = new Bitmap(240, 100);
    using (var g = Graphics.FromImage(bmp)) {
      g.Clear(Color.White);
      using var brush = new SolidBrush(Color.Black);
      using var font = new Font(FontFamily.GenericSansSerif, 24, FontStyle.Bold);
      g.DrawString("HELLO", font, brush, new PointF(10, 30));
    }

    var res = ocr.Recognize(bmp);
    res.Text.Should().NotBeNullOrWhiteSpace();
    res.Text!.ToUpperInvariant().Should().Contain("HELLO");
    res.Confidence.Should().BeGreaterThan(0.1);
  }

  [Fact]
  public void EmitsDebugLogWithCliContextWhenEnabled() {
    if (!IsTesseractAvailable()) return;
    Environment.SetEnvironmentVariable("GAMEBOT_USE_ADB", "false");
    Environment.SetEnvironmentVariable("GAMEBOT_TESSERACT_ENABLED", "true");
    Environment.SetEnvironmentVariable("Logging__LogLevel__Default", "Debug");
    Environment.SetEnvironmentVariable("Logging__LogLevel__GameBot__Service__Logging__TesseractInvocationLogger", "Debug");
    Environment.SetEnvironmentVariable("GAMEBOT_LOG_LEVEL__GameBot__Domain__Triggers__Evaluators__TesseractProcessOcr", "Debug");
    GameBot.IntegrationTests.TestEnvironment.PrepareCleanDataDir();

    using var provider = new TestLoggerProvider(LogLevel.Debug);
    using var baseFactory = new WebApplicationFactory<Program>();
    using var app = baseFactory.WithWebHostBuilder(builder => {
      builder.ConfigureLogging(logging => logging.AddProvider(provider));
    });
    using var scope = app.Services.CreateScope();
    var ocr = scope.ServiceProvider.GetRequiredService<GameBot.Domain.Triggers.Evaluators.ITextOcr>();

    using var bmp = LoadFixtureBitmap();
    _ = ocr.Recognize(bmp);

    provider.Entries.Should().Contain(entry =>
      entry.Category.Contains("TesseractInvocationLogger", StringComparison.Ordinal) &&
      entry.Level == LogLevel.Debug &&
      entry.Message.Contains("tesseract_invocation", StringComparison.OrdinalIgnoreCase) &&
      entry.Message.Contains("args=", StringComparison.OrdinalIgnoreCase));
  }

  [Fact]
  public void UsesInjectedRunnerForFailureScenario() {
    Environment.SetEnvironmentVariable("GAMEBOT_USE_ADB", "false");
    Environment.SetEnvironmentVariable("GAMEBOT_TESSERACT_ENABLED", "true");
    GameBot.IntegrationTests.TestEnvironment.PrepareCleanDataDir();

    var runner = new StubProcessRunner(() => new OcrResult("FAILURE", 0));
    using var baseFactory = new WebApplicationFactory<Program>();
    using var app = baseFactory.WithWebHostBuilder(builder => {
      builder.ConfigureServices(services => {
        services.AddSingleton<ITestOcrProcessRunner>(runner);
      });
    });
    using var scope = app.Services.CreateScope();
    var ocr = scope.ServiceProvider.GetRequiredService<ITextOcr>();
    using var bmp = new Bitmap(32, 32);

    var result = ocr.Recognize(bmp);

    result.Text.Should().Be("FAILURE");
    result.Confidence.Should().Be(0);
    runner.InvocationCount.Should().Be(1);
  }

  [Fact]
  public void ReturnsEmptyResultWhenRunnerTimesOut() {
    Environment.SetEnvironmentVariable("GAMEBOT_USE_ADB", "false");
    Environment.SetEnvironmentVariable("GAMEBOT_TESSERACT_ENABLED", "true");
    GameBot.IntegrationTests.TestEnvironment.PrepareCleanDataDir();

    var runner = new StubProcessRunner(() => new OcrResult("", 0), new TimeoutException("Simulated timeout"));
    using var baseFactory = new WebApplicationFactory<Program>();
    using var app = baseFactory.WithWebHostBuilder(builder => {
      builder.ConfigureServices(services => {
        services.AddSingleton<ITestOcrProcessRunner>(runner);
      });
    });
    using var scope = app.Services.CreateScope();
    var ocr = scope.ServiceProvider.GetRequiredService<ITextOcr>();
    using var bmp = new Bitmap(16, 16);

    var result = ocr.Recognize(bmp);

    result.Text.Should().BeEmpty();
    result.Confidence.Should().Be(0);
    runner.InvocationCount.Should().Be(1);
  }

  private static Bitmap LoadFixtureBitmap() {
    var path = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "TestAssets", "Ocr", "fixtures", "ocr", "sample-score.png");
    path = Path.GetFullPath(path);
    return new Bitmap(path);
  }

  private sealed class StubProcessRunner : ITestOcrProcessRunner {
    private readonly Func<OcrResult> _resultFactory;
    private readonly Exception? _exception;

    public StubProcessRunner(Func<OcrResult> resultFactory, Exception? exception = null) {
      _resultFactory = resultFactory;
      _exception = exception;
    }

    public int InvocationCount { get; private set; }

    public OcrResult Run(Bitmap image, string exePath, string lang, string? psm, string? oem, ILogger logger) {
      InvocationCount++;
      if (_exception is not null) {
        throw _exception;
      }
      return _resultFactory();
    }
  }
}
