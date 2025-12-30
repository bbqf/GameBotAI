using System.Diagnostics;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace GameBot.IntegrationTests.Images;

[Collection("ConfigIsolation")]
public sealed class OverwritePerformanceTests
{
  private const string WhitePng = "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO2n5u4AAAAASUVORK5CYII=";
  private const string BlackPng = "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAIAAACQd1PeAAAADElEQVR42mP8/5+hHgAHggJ/PqzZ/QAAAABJRU5ErkJggg==";

  public OverwritePerformanceTests()
  {
    Environment.SetEnvironmentVariable("GAMEBOT_USE_ADB", "false");
    Environment.SetEnvironmentVariable("GAMEBOT_DYNAMIC_PORT", "true");
    Environment.SetEnvironmentVariable("GAMEBOT_AUTH_TOKEN", "test-token");
    TestEnvironment.PrepareCleanDataDir();
  }

  [Fact]
  public async Task OverwriteCompletesWithinBudget()
  {
    using var app = new WebApplicationFactory<Program>();
    var client = app.CreateClient();
    client.DefaultRequestHeaders.Add("Authorization", "Bearer test-token");

    var create = await client.PostAsJsonAsync(new Uri("/api/images", UriKind.Relative), new { id = "perf-overwrite", data = WhitePng });
    create.EnsureSuccessStatusCode();

    var sw = Stopwatch.StartNew();
    var put = await client.PutAsJsonAsync(new Uri("/api/images/perf-overwrite", UriKind.Relative), new { data = BlackPng });
    sw.Stop();
    put.EnsureSuccessStatusCode();
    sw.Elapsed.TotalSeconds.Should().BeLessThan(5, "overwrite + save should complete within 5 seconds for small images");

    var after = await client.GetByteArrayAsync(new Uri("/api/images/perf-overwrite", UriKind.Relative));
    Convert.ToBase64String(after).Should().Be(BlackPng);
  }
}
