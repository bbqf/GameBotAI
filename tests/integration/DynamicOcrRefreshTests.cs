using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Net;
using System.Net.Http.Json;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace GameBot.IntegrationTests;

internal class DynamicOcrRefreshTests {
  private static bool IsTesseractAvailable() {
    var exe = Environment.GetEnvironmentVariable("GAMEBOT_TESSERACT_PATH");
    if (!string.IsNullOrWhiteSpace(exe) && File.Exists(exe)) return true;
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
  public async Task SwitchesFromEnvOcrToTesseractAfterRefresh() {
    if (!IsTesseractAvailable()) return; // skip when tesseract not available

    // Environment & data dir
    var prevUseAdb = Environment.GetEnvironmentVariable("GAMEBOT_USE_ADB");
    var prevDynPort = Environment.GetEnvironmentVariable("GAMEBOT_DYNAMIC_PORT");
    var prevAuth = Environment.GetEnvironmentVariable("GAMEBOT_AUTH_TOKEN");
    var prevTessEnabled = Environment.GetEnvironmentVariable("GAMEBOT_TESSERACT_ENABLED");
    var prevTestOcrText = Environment.GetEnvironmentVariable("GAMEBOT_TEST_OCR_TEXT");
    var prevDataDirVar = Environment.GetEnvironmentVariable("GAMEBOT_DATA_DIR");
    Environment.SetEnvironmentVariable("GAMEBOT_USE_ADB", "false");
    Environment.SetEnvironmentVariable("GAMEBOT_DYNAMIC_PORT", "true");
    Environment.SetEnvironmentVariable("GAMEBOT_AUTH_TOKEN", "test-token");
    Environment.SetEnvironmentVariable("GAMEBOT_TESSERACT_ENABLED", "false");
    Environment.SetEnvironmentVariable("GAMEBOT_TEST_OCR_TEXT", "NOPE");
    var dataDir = TestEnvironment.PrepareCleanDataDir();

    // Provide a screen image with text HELLO
    using (var bmp = new Bitmap(240, 100))
    using (var g = Graphics.FromImage(bmp))
    using (var brush = new SolidBrush(Color.Black))
    using (var font = new Font(FontFamily.GenericSansSerif, 24, FontStyle.Bold))
    using (var ms = new MemoryStream()) {
      g.Clear(Color.White);
      g.DrawString("HELLO", font, brush, new PointF(10, 30));
      bmp.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
      var b64 = Convert.ToBase64String(ms.ToArray());
      Environment.SetEnvironmentVariable("GAMEBOT_TEST_SCREEN_IMAGE_B64", "data:image/png;base64," + b64);
    }

    try {
      using var app = new WebApplicationFactory<Program>();
      var client = app.CreateClient();
      client.DefaultRequestHeaders.Add("Authorization", "Bearer test-token");

      // Create game
      var gameResp = await client.PostAsJsonAsync(new Uri("/games", UriKind.Relative), new { name = "G-DOCR", description = "d" }).ConfigureAwait(false);
      gameResp.StatusCode.Should().Be(HttpStatusCode.Created);
      var game = await gameResp.Content.ReadFromJsonAsync<Dictionary<string, object>>().ConfigureAwait(false);
      var gameId = game!["id"]!.ToString();


      // Create a text-match trigger for HELLO
      var trigCreate = new {
        type = "text-match",
        enabled = true,
        cooldownSeconds = 0,
        @params = new {
          target = "HELLO",
          region = new { x = 0.0, y = 0.0, width = 1.0, height = 1.0 },
          confidenceThreshold = 0.10,
          mode = "found"
        }
      };
      var tResp = await client.PostAsJsonAsync(new Uri("/triggers", UriKind.Relative), trigCreate).ConfigureAwait(false);
      tResp.StatusCode.Should().Be(HttpStatusCode.Created);
      var tBody = await tResp.Content.ReadFromJsonAsync<Dictionary<string, object>>().ConfigureAwait(false);
      var triggerId = tBody!["id"]!.ToString();

      // Initial test: with Env OCR and text "NOPE", should NOT be Satisfied
      var test1 = await client.PostAsync(new Uri($"/triggers/{triggerId}/test", UriKind.Relative), null).ConfigureAwait(false);
      test1.StatusCode.Should().Be(HttpStatusCode.OK);
      var res1 = await test1.Content.ReadFromJsonAsync<Dictionary<string, object>>().ConfigureAwait(false);
      ((System.Text.Json.JsonElement)res1!["status"]).GetString().Should().NotBe("Satisfied");

      // Modify saved config on disk to enable Tesseract
      var cfgDir = Path.Combine(dataDir, "config");
      var cfgFile = Path.Combine(cfgDir, "config.json");
      Directory.CreateDirectory(cfgDir);
      var newJson = "{\n  \"parameters\": {\n    \"GAMEBOT_TESSERACT_ENABLED\": { \"value\": \"true\" }\n  }\n}";
      await File.WriteAllTextAsync(cfgFile, newJson).ConfigureAwait(false);

      // Refresh to apply new configuration
      var refresh = await client.PostAsync(new Uri("/config/refresh", UriKind.Relative), null).ConfigureAwait(false);
      refresh.StatusCode.Should().Be(HttpStatusCode.OK);

      // Second test: with Tesseract OCR and a HELLO screen, should be Satisfied
      var test2 = await client.PostAsync(new Uri($"/triggers/{triggerId}/test", UriKind.Relative), null).ConfigureAwait(false);
      test2.StatusCode.Should().Be(HttpStatusCode.OK);
      var res2 = await test2.Content.ReadFromJsonAsync<Dictionary<string, object>>().ConfigureAwait(false);
      ((System.Text.Json.JsonElement)res2!["status"]).GetString().Should().Be("Satisfied");
    }
    finally {
      Environment.SetEnvironmentVariable("GAMEBOT_USE_ADB", prevUseAdb);
      Environment.SetEnvironmentVariable("GAMEBOT_DYNAMIC_PORT", prevDynPort);
      Environment.SetEnvironmentVariable("GAMEBOT_AUTH_TOKEN", prevAuth);
      Environment.SetEnvironmentVariable("GAMEBOT_TESSERACT_ENABLED", prevTessEnabled);
      Environment.SetEnvironmentVariable("GAMEBOT_TEST_OCR_TEXT", prevTestOcrText);
      Environment.SetEnvironmentVariable("GAMEBOT_DATA_DIR", prevDataDirVar);
    }
  }
}
