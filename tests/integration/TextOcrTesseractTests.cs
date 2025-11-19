using System;
using System.Diagnostics;
using System.Drawing;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
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
}
