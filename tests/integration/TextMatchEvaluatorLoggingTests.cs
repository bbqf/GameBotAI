using System;
using System.Drawing;
using System.IO;
using System.Net;
using System.Net.Http.Json;
using System.Threading.Tasks;
using FluentAssertions;
using GameBot.IntegrationTests.Helpers;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Logging;
using Xunit;

namespace GameBot.IntegrationTests;

public class TextMatchEvaluatorLoggingTests
{
    private sealed class LoggingWebAppFactory : WebApplicationFactory<Program>
    {
        private readonly TestLoggerProvider _provider;
        public LoggingWebAppFactory(TestLoggerProvider provider)
        {
            _provider = provider;
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.ConfigureLogging(lb =>
            {
                lb.SetMinimumLevel(LogLevel.Debug);
                lb.AddProvider(_provider);
            });
        }
    }

    [Fact]
    public async Task CroppedSizeLogAppearsAndIsNot1x1()
    {
        // Arrange environment to use the in-memory screen source
        Environment.SetEnvironmentVariable("GAMEBOT_USE_ADB", "false");
        Environment.SetEnvironmentVariable("GAMEBOT_DYNAMIC_PORT", "true");
        Environment.SetEnvironmentVariable("GAMEBOT_AUTH_TOKEN", "test-token");

        // Provide a screen image (240x100) with clear text area
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

        // Capture logs
        var provider = new TestLoggerProvider(LogLevel.Debug);
        using var app = new LoggingWebAppFactory(provider);
        var client = app.CreateClient();
        client.DefaultRequestHeaders.Add("Authorization", "Bearer test-token");

        // Create game
        var gameResp = await client.PostAsJsonAsync(new Uri("/games", UriKind.Relative), new { name = "G-Log", description = "d" }).ConfigureAwait(true);
        gameResp.StatusCode.Should().Be(HttpStatusCode.Created);
        var game = await gameResp.Content.ReadFromJsonAsync<Dictionary<string, object>>().ConfigureAwait(true);
        var gameId = game!["id"]!.ToString();

        // Create profile
        var profResp = await client.PostAsJsonAsync(new Uri("/profiles", UriKind.Relative), new { name = "P-Log", gameId, steps = Array.Empty<object>() }).ConfigureAwait(true);
        profResp.StatusCode.Should().Be(HttpStatusCode.Created);
        var prof = await profResp.Content.ReadFromJsonAsync<Dictionary<string, object>>().ConfigureAwait(true);
        var profileId = prof!["id"]!.ToString();

        // Create a text-match trigger over full screen
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
        var tResp = await client.PostAsJsonAsync(new Uri($"/profiles/{profileId}/triggers", UriKind.Relative), trigCreate).ConfigureAwait(true);
        tResp.StatusCode.Should().Be(HttpStatusCode.Created);
        var tBody = await tResp.Content.ReadFromJsonAsync<Dictionary<string, object>>().ConfigureAwait(true);
        var triggerId = tBody!["id"]!.ToString();

        // Act: Test trigger - should be Satisfied and produce logs we can assert on
        var testResp = await client.PostAsync(new Uri($"/profiles/{profileId}/triggers/{triggerId}/test", UriKind.Relative), null).ConfigureAwait(true);
        testResp.StatusCode.Should().Be(HttpStatusCode.OK);

        // Assert log contains cropped size and not 1x1
        provider.Entries.Should().Contain(e =>
            e.Category.Contains("TextMatchEvaluator") && e.Message.Contains("Cropped image size"));
        provider.Entries.Should().NotContain(e =>
            e.Category.Contains("TextMatchEvaluator") && e.Message.Contains("Cropped image size 1x1"));
    }
}
