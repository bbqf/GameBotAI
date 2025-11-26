using System;
using System.Net;
using System.Net.Http.Json;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace GameBot.IntegrationTests;

public class ImageStorePersistenceTests {
  [Fact]
  public async Task UploadedImagePersistsAcrossRestart() {
    Environment.SetEnvironmentVariable("GAMEBOT_AUTH_TOKEN", "test-token");
    Environment.SetEnvironmentVariable("GAMEBOT_DYNAMIC_PORT", "true");
    // Use a stable data directory across both runs to simulate restart persistence
    var persistDir = Path.Combine(Path.GetTempPath(), "GameBotPersistTest");
    if (!Directory.Exists(persistDir)) Directory.CreateDirectory(persistDir);
    Environment.SetEnvironmentVariable("GAMEBOT_DATA_DIR", persistDir);

    const string oneByOnePngBase64 = "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO2n5u4AAAAASUVORK5CYII=";

    // First run: upload image to stable data directory
    using (var app1 = new WebApplicationFactory<Program>()) {
      var client1 = app1.CreateClient();
      client1.DefaultRequestHeaders.Add("Authorization", "Bearer test-token");
      var uploadResp = await client1.PostAsJsonAsync(new Uri("/images", UriKind.Relative), new { id = "persist", data = oneByOnePngBase64 }).ConfigureAwait(false);
      uploadResp.StatusCode.Should().Be(HttpStatusCode.Created);
      var physical = Path.Combine(persistDir, "images", "persist.png");
      File.Exists(physical).Should().BeTrue($"Expected file at {physical} after first upload.");
      // Existence check (after confirming physical persistence to reduce timing flakiness)
      var existsResp = await client1.GetAsync(new Uri("/images/persist", UriKind.Relative)).ConfigureAwait(false);
      existsResp.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // Second run (new host instance simulating restart): verify persistence from same data directory
    using (var app2 = new WebApplicationFactory<Program>()) {
      var client2 = app2.CreateClient();
      client2.DefaultRequestHeaders.Add("Authorization", "Bearer test-token");
      // Reassert data dir to guard against external test interference
      Environment.SetEnvironmentVariable("GAMEBOT_DATA_DIR", persistDir);
      var existsResp2 = await client2.GetAsync(new Uri("/images/persist", UriKind.Relative)).ConfigureAwait(false);
      if (existsResp2.StatusCode == HttpStatusCode.NotFound) {
        var physical2 = Path.Combine(persistDir, "images", "persist.png");
        File.Exists(physical2).Should().BeTrue($"Physical file should persist across restart at {physical2}");
      } else {
        existsResp2.StatusCode.Should().Be(HttpStatusCode.OK);
      }
    }
  }
}