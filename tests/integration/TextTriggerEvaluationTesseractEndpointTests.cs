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

public class TextTriggerEvaluationTesseractEndpointTests
{
    private static bool IsTesseractAvailable()
    {
        var exe = Environment.GetEnvironmentVariable("GAMEBOT_TESSERACT_PATH");
        if (!string.IsNullOrWhiteSpace(exe) && File.Exists(exe)) return true;
        try
        {
            var psi = new ProcessStartInfo { FileName = "tesseract", Arguments = "--version", UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
            using var p = Process.Start(psi);
            if (p == null) return false;
            p.WaitForExit(2000);
            return p.HasExited && p.ExitCode == 0;
        }
        catch { return false; }
    }

    [Fact]
    public async Task TextMatchTriggerTestEndpointSatisfiedWithTesseract()
    {
        if (!IsTesseractAvailable()) return; // Skip if tesseract not available

        // Prepare env
        Environment.SetEnvironmentVariable("GAMEBOT_USE_ADB", "false");
        Environment.SetEnvironmentVariable("GAMEBOT_DYNAMIC_PORT", "true");
        Environment.SetEnvironmentVariable("GAMEBOT_AUTH_TOKEN", "test-token");
        Environment.SetEnvironmentVariable("GAMEBOT_TESSERACT_ENABLED", "true");

        // Generate an in-memory image with text HELLO and provide as screen source
        using (var bmp = new Bitmap(240, 100))
        using (var g = Graphics.FromImage(bmp))
        using (var brush = new SolidBrush(Color.Black))
        using (var font = new Font(FontFamily.GenericSansSerif, 24, FontStyle.Bold))
        using (var ms = new MemoryStream())
        {
            g.Clear(Color.White);
            g.DrawString("HELLO", font, brush, new PointF(10, 30));
            bmp.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
            var b64 = Convert.ToBase64String(ms.ToArray());
            Environment.SetEnvironmentVariable("GAMEBOT_TEST_SCREEN_IMAGE_B64", "data:image/png;base64," + b64);
        }

        using var app = new WebApplicationFactory<Program>();
        var client = app.CreateClient();
        client.DefaultRequestHeaders.Add("Authorization", "Bearer test-token");

        // Create game
        var gameResp = await client.PostAsJsonAsync(new Uri("/games", UriKind.Relative), new { name = "G-Text", description = "d" });
        gameResp.StatusCode.Should().Be(HttpStatusCode.Created);
        var game = await gameResp.Content.ReadFromJsonAsync<Dictionary<string, object>>();
        var gameId = game!["id"]!.ToString();

        // (legacy profile removed)

        // Create a text-match trigger for HELLO, region full screen
        var trigCreate = new
        {
            type = "text-match",
            enabled = true,
            cooldownSeconds = 0,
            @params = new
            {
                target = "HELLO",
                region = new { x = 0.0, y = 0.0, width = 1.0, height = 1.0 },
                confidenceThreshold = 0.10,
                mode = "found"
            }
        };
        var tResp = await client.PostAsJsonAsync(new Uri("/triggers", UriKind.Relative), trigCreate);
        tResp.StatusCode.Should().Be(HttpStatusCode.Created);
        var tBody = await tResp.Content.ReadFromJsonAsync<Dictionary<string, object>>();
        var triggerId = tBody!["id"]!.ToString();

        // Test trigger - should be Satisfied
        var testResp = await client.PostAsync(new Uri($"/triggers/{triggerId}/test", UriKind.Relative), null);
        testResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var res = await testResp.Content.ReadFromJsonAsync<Dictionary<string, object>>();
        ((System.Text.Json.JsonElement)res!["status"]).GetString().Should().Be("Satisfied");
    }
}
