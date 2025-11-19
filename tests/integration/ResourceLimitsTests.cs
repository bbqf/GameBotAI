using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace GameBot.IntegrationTests;

internal sealed class ResourceLimitsTests : IDisposable {
  private readonly string? _prevUseAdb;
  private readonly string? _prevDynPort;
  private readonly string? _prevDataDir;
  public ResourceLimitsTests() {
    _prevUseAdb = Environment.GetEnvironmentVariable("GAMEBOT_USE_ADB");
    _prevDynPort = Environment.GetEnvironmentVariable("GAMEBOT_DYNAMIC_PORT");
    _prevDataDir = Environment.GetEnvironmentVariable("GAMEBOT_DATA_DIR");
    Environment.SetEnvironmentVariable("GAMEBOT_USE_ADB", "false");
    Environment.SetEnvironmentVariable("GAMEBOT_DYNAMIC_PORT", "true");
    TestEnvironment.PrepareCleanDataDir();
  }

  public void Dispose() {
    Environment.SetEnvironmentVariable("GAMEBOT_USE_ADB", _prevUseAdb);
    Environment.SetEnvironmentVariable("GAMEBOT_DYNAMIC_PORT", _prevDynPort);
    Environment.SetEnvironmentVariable("GAMEBOT_DATA_DIR", _prevDataDir);
    GC.SuppressFinalize(this);
  }

  [Fact]
  public async Task CreatingSessionBeyondCapacityReturns429() {
    // Configure capacity to 1 via environment so the app binds it on startup
    var prevMax = Environment.GetEnvironmentVariable("Service__Sessions__MaxConcurrentSessions");
    var prevToken = Environment.GetEnvironmentVariable("GAMEBOT_AUTH_TOKEN");
    Environment.SetEnvironmentVariable("Service__Sessions__MaxConcurrentSessions", "1");
    Environment.SetEnvironmentVariable("GAMEBOT_AUTH_TOKEN", "test-token");
    try {
      using var app = new WebApplicationFactory<Program>();
      var client = app.CreateClient();
      client.DefaultRequestHeaders.Add("Authorization", "Bearer test-token");

      var devs = await client.GetFromJsonAsync<List<Dictionary<string, object>>>(new Uri("/adb/devices", UriKind.Relative)).ConfigureAwait(true);
      if (devs is null || devs.Count == 0) return;
      var serial = devs[0]["serial"]!.ToString();
      var first = await client.PostAsJsonAsync(new Uri("/sessions", UriKind.Relative), new { gameId = "g1", adbSerial = serial }).ConfigureAwait(true);
      first.StatusCode.Should().Be(HttpStatusCode.Created);

      var second = await client.PostAsJsonAsync(new Uri("/sessions", UriKind.Relative), new { gameId = "g2", adbSerial = serial }).ConfigureAwait(true);
      second.StatusCode.Should().Be((HttpStatusCode)429);
    }
    finally {
      Environment.SetEnvironmentVariable("Service__Sessions__MaxConcurrentSessions", prevMax);
      Environment.SetEnvironmentVariable("GAMEBOT_AUTH_TOKEN", prevToken);
    }
  }

  [Fact]
  public async Task IdleSessionIsEvictedAfterTimeout() {
    // Set idle timeout to 1 second for test
    var prevTimeout = Environment.GetEnvironmentVariable("Service__Sessions__IdleTimeoutSeconds");
    var prevToken2 = Environment.GetEnvironmentVariable("GAMEBOT_AUTH_TOKEN");
    Environment.SetEnvironmentVariable("Service__Sessions__IdleTimeoutSeconds", "1");
    Environment.SetEnvironmentVariable("GAMEBOT_AUTH_TOKEN", "test-token");
    try {
      using var app = new WebApplicationFactory<Program>();
      var client = app.CreateClient();
      client.DefaultRequestHeaders.Add("Authorization", "Bearer test-token");

      var devs2 = await client.GetFromJsonAsync<List<Dictionary<string, object>>>(new Uri("/adb/devices", UriKind.Relative)).ConfigureAwait(true);
      if (devs2 is null || devs2.Count == 0) return;
      var serial2 = devs2[0]["serial"]!.ToString();
      var createResp = await client.PostAsJsonAsync(new Uri("/sessions", UriKind.Relative), new { gameId = "g1", adbSerial = serial2 }).ConfigureAwait(true);
      createResp.EnsureSuccessStatusCode();
      var created = await createResp.Content.ReadFromJsonAsync<Dictionary<string, object>>().ConfigureAwait(true);
      var id = created!["id"].ToString();

      // Wait beyond idle timeout
      await Task.Delay(1500).ConfigureAwait(true);

      var getResp = await client.GetAsync(new Uri($"/sessions/{id}", UriKind.Relative)).ConfigureAwait(true);
      getResp.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
    finally {
      Environment.SetEnvironmentVariable("Service__Sessions__IdleTimeoutSeconds", prevTimeout);
      Environment.SetEnvironmentVariable("GAMEBOT_AUTH_TOKEN", prevToken2);
    }
  }
}
