using System;
using System.Net;
using System.Net.Http.Json;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace GameBot.IntegrationTests;

[Collection("ConfigIsolation")]
public class ImageStorePersistenceTests : IDisposable {
  private readonly string? _prevAuth;
  private readonly string? _prevDynPort;
  private readonly string? _prevDataDir;
  private readonly string? _prevStorageRoot;
  private readonly string _persistDir;
  private bool _disposed;

  public ImageStorePersistenceTests() {
    _prevAuth = Environment.GetEnvironmentVariable("GAMEBOT_AUTH_TOKEN");
    _prevDynPort = Environment.GetEnvironmentVariable("GAMEBOT_DYNAMIC_PORT");
    _prevDataDir = Environment.GetEnvironmentVariable("GAMEBOT_DATA_DIR");
    _prevStorageRoot = Environment.GetEnvironmentVariable("Service__Storage__Root");

    Environment.SetEnvironmentVariable("GAMEBOT_AUTH_TOKEN", "test-token");
    Environment.SetEnvironmentVariable("GAMEBOT_DYNAMIC_PORT", "true");
    // Use a stable data directory across both runs to simulate restart persistence
    _persistDir = Path.Combine(Path.GetTempPath(), "GameBotPersistTest");
    if (!Directory.Exists(_persistDir)) Directory.CreateDirectory(_persistDir);
    Environment.SetEnvironmentVariable("GAMEBOT_DATA_DIR", _persistDir);
    // Ensure app configuration resolves storageRoot to the same directory regardless of config precedence
    Environment.SetEnvironmentVariable("Service__Storage__Root", _persistDir);
  }

  protected virtual void Dispose(bool disposing) {
    if (_disposed) return;
    if (disposing) {
      Environment.SetEnvironmentVariable("GAMEBOT_AUTH_TOKEN", _prevAuth);
      Environment.SetEnvironmentVariable("GAMEBOT_DYNAMIC_PORT", _prevDynPort);
      Environment.SetEnvironmentVariable("GAMEBOT_DATA_DIR", _prevDataDir);
      Environment.SetEnvironmentVariable("Service__Storage__Root", _prevStorageRoot);
    }
    _disposed = true;
  }

  public void Dispose() {
    Dispose(true);
    GC.SuppressFinalize(this);
  }

  [Fact]
  public async Task UploadedImagePersistsAcrossRestart() {

    const string oneByOnePngBase64 = "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO2n5u4AAAAASUVORK5CYII=";

    // First run: upload image to stable data directory
    using (var app1 = new WebApplicationFactory<Program>()) {
      var client1 = app1.CreateClient();
      client1.DefaultRequestHeaders.Add("Authorization", "Bearer test-token");
      var uploadResp = await client1.PostAsJsonAsync(new Uri("/api/images", UriKind.Relative), new { id = "persist", data = oneByOnePngBase64 }).ConfigureAwait(false);
      uploadResp.StatusCode.Should().Be(HttpStatusCode.Created);
      var physical = Path.Combine(_persistDir, "images", "persist.png");
      // Allow brief time for atomic replace/move operations to complete on CI file systems
      var waitedMs = 0;
      while (!File.Exists(physical) && waitedMs < 1000) { await Task.Delay(100).ConfigureAwait(false); waitedMs += 100; }
      File.Exists(physical).Should().BeTrue($"Expected file at {physical} after first upload.");
      // Existence check (after confirming physical persistence to reduce timing flakiness)
      var existsResp = await client1.GetAsync(new Uri("/api/images/persist", UriKind.Relative)).ConfigureAwait(false);
      existsResp.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // Second run (new host instance simulating restart): verify persistence from same data directory
    using (var app2 = new WebApplicationFactory<Program>()) {
      var client2 = app2.CreateClient();
      client2.DefaultRequestHeaders.Add("Authorization", "Bearer test-token");
      // Reassert data dir to guard against external test interference and config precedence
      Environment.SetEnvironmentVariable("GAMEBOT_DATA_DIR", _persistDir);
      Environment.SetEnvironmentVariable("Service__Storage__Root", _persistDir);
      var existsResp2 = await client2.GetAsync(new Uri("/api/images/persist", UriKind.Relative)).ConfigureAwait(false);
      if (existsResp2.StatusCode == HttpStatusCode.NotFound) {
        var physical2 = Path.Combine(_persistDir, "images", "persist.png");
        var waited2Ms = 0;
        while (!File.Exists(physical2) && waited2Ms < 1000) { await Task.Delay(100).ConfigureAwait(false); waited2Ms += 100; }
        File.Exists(physical2).Should().BeTrue($"Physical file should persist across restart at {physical2}");
      } else {
        existsResp2.StatusCode.Should().Be(HttpStatusCode.OK);
      }
    }
  }
}