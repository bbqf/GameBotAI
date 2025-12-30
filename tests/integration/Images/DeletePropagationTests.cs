using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace GameBot.IntegrationTests.Images;

[Collection("ConfigIsolation")]
public sealed class DeletePropagationTests
{
  private const string OneByOnePngBase64 = "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO2n5u4AAAAASUVORK5CYII=";

  public DeletePropagationTests()
  {
    Environment.SetEnvironmentVariable("GAMEBOT_USE_ADB", "false");
    Environment.SetEnvironmentVariable("GAMEBOT_DYNAMIC_PORT", "true");
    Environment.SetEnvironmentVariable("GAMEBOT_AUTH_TOKEN", "test-token");
    TestEnvironment.PrepareCleanDataDir();
  }

  [Fact]
  public async Task DeleteRemovesFromListQuickly()
  {
    using var app = new WebApplicationFactory<Program>();
    var client = app.CreateClient();
    client.DefaultRequestHeaders.Add("Authorization", "Bearer test-token");

    await client.PostAsJsonAsync(new Uri("/api/images", UriKind.Relative), new { id = "propagate-me", data = OneByOnePngBase64 });

    var del = await client.DeleteAsync(new Uri("/api/images/propagate-me", UriKind.Relative));
    del.StatusCode.Should().Be(HttpStatusCode.NoContent);

    var sw = Stopwatch.StartNew();
    while (true)
    {
      var list = await client.GetFromJsonAsync<ImageList>(new Uri("/api/images", UriKind.Relative));
      if (list is not null && (list.Ids is null || !list.Ids.Contains("propagate-me", StringComparer.OrdinalIgnoreCase)))
      {
        break;
      }
      if (sw.Elapsed.TotalSeconds > 2) break;
      await Task.Delay(50);
    }
    sw.Stop();
    sw.Elapsed.TotalSeconds.Should().BeLessThan(2, "deleted image should stop appearing in list quickly");

    var detail = await client.GetAsync(new Uri("/api/images/propagate-me", UriKind.Relative));
    detail.StatusCode.Should().Be(HttpStatusCode.NotFound);
  }

  private sealed record ImageList(string[]? Ids);
}
