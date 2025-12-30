using System.Diagnostics;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace GameBot.IntegrationTests.Images;

[Collection("ConfigIsolation")]
public sealed class DetailPerformanceTests
{
  private const string OneByOnePngBase64 = "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO2n5u4AAAAASUVORK5CYII=";

  public DetailPerformanceTests()
  {
    Environment.SetEnvironmentVariable("GAMEBOT_USE_ADB", "false");
    Environment.SetEnvironmentVariable("GAMEBOT_DYNAMIC_PORT", "true");
    Environment.SetEnvironmentVariable("GAMEBOT_AUTH_TOKEN", "test-token");
    TestEnvironment.PrepareCleanDataDir();
  }

  [Fact]
  public async Task GetImageCompletesWithinBudget()
  {
    using var app = new WebApplicationFactory<Program>();
    var client = app.CreateClient();
    client.DefaultRequestHeaders.Add("Authorization", "Bearer test-token");

    var up = await client.PostAsJsonAsync(new Uri("/api/images", UriKind.Relative), new { id = "perf-detail", data = OneByOnePngBase64 });
    up.EnsureSuccessStatusCode();

    var sw = Stopwatch.StartNew();
    var resp = await client.GetAsync(new Uri("/api/images/perf-detail", UriKind.Relative));
    sw.Stop();
    resp.EnsureSuccessStatusCode();
    sw.Elapsed.TotalSeconds.Should().BeLessThan(2, "detail preview should return within 2 seconds for small images");
  }
}
