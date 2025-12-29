using System;
using System.Linq;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace GameBot.Tests.Integration
{
    public class ImageDetectionsOrderingTests
    {
        

        [Fact]
        public async Task DetectReturnsDeterministicOrdering()
        {
            // Match setup similar to other integration tests
            const string oneByOnePngBase64 = "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO2n5u4AAAAASUVORK5CYII=";
            Environment.SetEnvironmentVariable("GAMEBOT_USE_ADB", "false");
            Environment.SetEnvironmentVariable("GAMEBOT_AUTH_TOKEN", "test-token");
            Environment.SetEnvironmentVariable("GAMEBOT_DYNAMIC_PORT", "true");
            Environment.SetEnvironmentVariable("GAMEBOT_TEST_SCREEN_IMAGE_B64", oneByOnePngBase64);
            GameBot.IntegrationTests.TestEnvironment.PrepareCleanDataDir();
            using var app = new WebApplicationFactory<Program>();
            var client = app.CreateClient();
            client.DefaultRequestHeaders.Add("Authorization", "Bearer test-token");
            // Seed a template that matches the screenshot exactly
            var uploadResp = await client.PostAsJsonAsync(new Uri("/api/images", UriKind.Relative), new { id = "tplStable", data = oneByOnePngBase64 });
            uploadResp.StatusCode.Should().Be(HttpStatusCode.Created);
            var req = new
            {
                referenceImageId = "tplStable",
                threshold = 0.5,
                maxResults = 20,
                overlap = 0.3
            };

            // Run detection multiple times and compare ordering
            var runs = 5;
            JsonDocument? first = null;

            for (var i = 0; i < runs; i++)
            {
                var resp = await client.PostAsJsonAsync(new Uri("/api/images/detect", UriKind.Relative), req);
                resp.StatusCode.Should().Be(HttpStatusCode.OK);
                var json = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());

                var matches = json.RootElement.GetProperty("matches");
                if (first is null)
                {
                    first = json;
                }
                else
                {
                    // Ensure same count
                    matches.GetArrayLength().Should().Be(first.RootElement.GetProperty("matches").GetArrayLength());

                    // Ensure ordering is identical for all elements (confidence desc and stable tie-breaks)
                    var a = first.RootElement.GetProperty("matches");
                    var b = matches;
                    for (int j = 0; j < a.GetArrayLength(); j++)
                    {
                        var aj = a[j];
                        var bj = b[j];
                        aj.GetProperty("confidence").GetDouble().Should().BeApproximately(bj.GetProperty("confidence").GetDouble(), 1e-12);
                        // Stable tiebreaker: compare bbox to ensure consistent order
                        aj.GetProperty("bbox").GetProperty("x").GetDouble().Should().BeApproximately(bj.GetProperty("bbox").GetProperty("x").GetDouble(), 1e-12);
                        aj.GetProperty("bbox").GetProperty("y").GetDouble().Should().BeApproximately(bj.GetProperty("bbox").GetProperty("y").GetDouble(), 1e-12);
                        aj.GetProperty("bbox").GetProperty("width").GetDouble().Should().BeApproximately(bj.GetProperty("bbox").GetProperty("width").GetDouble(), 1e-12);
                        aj.GetProperty("bbox").GetProperty("height").GetDouble().Should().BeApproximately(bj.GetProperty("bbox").GetProperty("height").GetDouble(), 1e-12);
                    }
                }
            }
        }
    }
}