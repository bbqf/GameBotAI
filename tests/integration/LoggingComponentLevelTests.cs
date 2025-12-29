using System;
using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using GameBot.Domain.Services.Logging;
using GameBot.Service.Logging;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace GameBot.IntegrationTests;

#pragma warning disable CA1848

[Collection("ConfigIsolation")]
public sealed class LoggingComponentLevelTests : IDisposable
{
  private readonly string? _prevUseAdb;
  private readonly string? _prevAuthToken;
  private readonly string? _prevDataDir;
  private readonly string? _prevDynamicPort;

  public LoggingComponentLevelTests()
  {
    _prevUseAdb = Environment.GetEnvironmentVariable("GAMEBOT_USE_ADB");
    _prevAuthToken = Environment.GetEnvironmentVariable("GAMEBOT_AUTH_TOKEN");
    _prevDataDir = Environment.GetEnvironmentVariable("GAMEBOT_DATA_DIR");
    _prevDynamicPort = Environment.GetEnvironmentVariable("GAMEBOT_DYNAMIC_PORT");

    Environment.SetEnvironmentVariable("GAMEBOT_USE_ADB", "false");
    Environment.SetEnvironmentVariable("GAMEBOT_AUTH_TOKEN", "test-token");
    Environment.SetEnvironmentVariable("GAMEBOT_DYNAMIC_PORT", "true");
    TestEnvironment.PrepareCleanDataDir();
  }

  public void Dispose()
  {
    Environment.SetEnvironmentVariable("GAMEBOT_USE_ADB", _prevUseAdb);
    Environment.SetEnvironmentVariable("GAMEBOT_AUTH_TOKEN", _prevAuthToken);
    Environment.SetEnvironmentVariable("GAMEBOT_DATA_DIR", _prevDataDir);
    Environment.SetEnvironmentVariable("GAMEBOT_DYNAMIC_PORT", _prevDynamicPort);
    GC.SuppressFinalize(this);
  }

  [Fact]
  public async Task ComponentLevelChangesAffectRuntimeLogging()
  {
    using var app = new WebApplicationFactory<Program>();
    using var client = CreateAuthedClient(app);
    using var scope = app.Server.Services.CreateScope();
    var gate = scope.ServiceProvider.GetRequiredService<LoggingPolicyGate>();
    gate.ShouldLog(provider: null, category: "GameBot.Service", LogLevel.Debug).Should().BeFalse("default policy keeps GameBot.Service at Warning");

    var resp = await client.PutAsJsonAsync(
      "/api/config/logging/components/GameBot.Service",
      new { level = "Debug", notes = "integration" }).ConfigureAwait(true);
    resp.StatusCode.Should().Be(HttpStatusCode.OK);

    var policyService = scope.ServiceProvider.GetRequiredService<IRuntimeLoggingPolicyService>();
    var snapshot = await policyService.GetSnapshotAsync().ConfigureAwait(true);
    snapshot.Components.Should().Contain(component =>
      component.Name == "GameBot.Service" &&
      component.EffectiveLevel == LogLevel.Debug &&
      component.Enabled,
      "component override should persist with Debug level");

    gate.ShouldLog(provider: null, category: "GameBot.Service", LogLevel.Debug).Should().BeTrue("runtime logging gate should allow Debug after override");

    gate.ShouldLog(provider: null, category: "GameBot.Service", LogLevel.Debug).Should().BeTrue("runtime logging gate should allow Debug after override");
  }

  private static HttpClient CreateAuthedClient(WebApplicationFactory<Program> app)
  {
    var client = app.CreateClient();
    client.DefaultRequestHeaders.Add("Authorization", "Bearer test-token");
    return client;
  }
}

#pragma warning restore CA1848