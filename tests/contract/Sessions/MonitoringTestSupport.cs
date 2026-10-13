#pragma warning disable CA2007 // test code: no ConfigureAwait
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using GameBot.Emulator.Adb;
using GameBot.Service.Services.EnsureEmulatorRunning;
using GameBot.Service.Services.Liveness;
using GameBot.Service.Services.Monitoring;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace GameBot.ContractTests.Sessions;

/// <summary>
/// Feature 141: the shared set-up of the monitoring contract tests. It sets the environment of the test host
/// and restores it on dispose. Each test uses its own host and replaces the device services with fakes.
/// </summary>
public abstract class MonitoringContractTestBase : IDisposable {
  private readonly string? _prevAuthToken;
  private readonly string? _prevUseAdb;
  private readonly string? _prevDynamicPort;

  protected MonitoringContractTestBase() {
    _prevAuthToken = Environment.GetEnvironmentVariable("GAMEBOT_AUTH_TOKEN");
    _prevUseAdb = Environment.GetEnvironmentVariable("GAMEBOT_USE_ADB");
    _prevDynamicPort = Environment.GetEnvironmentVariable("GAMEBOT_DYNAMIC_PORT");
    Environment.SetEnvironmentVariable("GAMEBOT_AUTH_TOKEN", "test-token");
    Environment.SetEnvironmentVariable("GAMEBOT_USE_ADB", "false");
    Environment.SetEnvironmentVariable("GAMEBOT_DYNAMIC_PORT", "true");
  }

  public void Dispose() {
    Dispose(true);
    GC.SuppressFinalize(this);
  }

  protected virtual void Dispose(bool disposing) {
    if (!disposing) return;
    Environment.SetEnvironmentVariable("GAMEBOT_AUTH_TOKEN", _prevAuthToken);
    Environment.SetEnvironmentVariable("GAMEBOT_USE_ADB", _prevUseAdb);
    Environment.SetEnvironmentVariable("GAMEBOT_DYNAMIC_PORT", _prevDynamicPort);
  }

  private protected static HttpClient Client(WebApplicationFactory<Program> app) {
    var client = app.CreateClient();
    client.DefaultRequestHeaders.Add("Authorization", "Bearer test-token");
    return client;
  }

  private protected static WebApplicationFactory<Program> Factory(WebApplicationFactory<Program> baseFactory, Action<IServiceCollection> configure) =>
    baseFactory.WithWebHostBuilder(b => b.ConfigureTestServices(configure));

  /// <summary>Replaces the device services of the restart with fakes and sets a fast restart.</summary>
  private protected static void UseRestartFakes(
      IServiceCollection services, LivenessFakeSessionManager sessions, FakeEmulatorControl control, FakeReconnectorDouble reconnector) {
    services.RemoveAll<GameBot.Emulator.Session.ISessionManager>();
    services.AddSingleton<GameBot.Emulator.Session.ISessionManager>(sessions);
    services.RemoveAll<IEmulatorControl>();
    services.AddSingleton<IEmulatorControl>(control);
    services.RemoveAll<IEmulatorDeviceProbe>();
    services.AddSingleton<IEmulatorDeviceProbe>(new FakeDeviceProbe());
    services.RemoveAll<ISessionReconnector>();
    services.AddSingleton<ISessionReconnector>(reconnector);
    services.RemoveAll<ISessionDirectCapture>();
    services.AddSingleton<ISessionDirectCapture>(new FixedDirectCapture());
    services.Configure<SessionMonitoringOptions>(o => {
      o.RestartPollIntervalMs = 1;
      o.RestartTimeoutMs = 10000;
    });
  }

  private protected static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response) =>
    JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone();
}

/// <summary>A fake ldconsole control. A gate can hold the reboot, to test a slow restart.</summary>
internal sealed class FakeEmulatorControl : IEmulatorControl {
  public bool IsAvailable { get; set; } = true;
  public LdConsoleRunState RunState { get; set; } = LdConsoleRunState.Running;
  public TaskCompletionSource<bool>? RebootGate { get; set; }
  public List<int?> RebootedIndexes { get; } = new();

  public Task<LdConsoleRunState> GetRunStateAsync(string? instanceName, int? instanceIndex, CancellationToken ct = default) =>
    Task.FromResult(RunState);

  public Task LaunchAsync(string? instanceName, int? instanceIndex, CancellationToken ct = default) => Task.CompletedTask;

  public async Task<bool> RebootAsync(string? instanceName, int? instanceIndex, CancellationToken ct = default) {
    lock (RebootedIndexes) RebootedIndexes.Add(instanceIndex);
    if (RebootGate is { } gate) await gate.Task.WaitAsync(ct).ConfigureAwait(false);
    return true;
  }
}

internal sealed class FakeDeviceProbe : IEmulatorDeviceProbe {
  public bool IsAvailable => true;
  public Task<bool> IsResponsiveAsync(string adbSerial, CancellationToken ct = default) => Task.FromResult(true);
}

/// <summary>A fake reconnector that records each call.</summary>
internal sealed class FakeReconnectorDouble : ISessionReconnector {
  public List<(string SessionId, string Serial)> Calls { get; } = new();

  public Task ReconnectAsync(string sessionId, string deviceSerial, CancellationToken ct = default) {
    lock (Calls) Calls.Add((sessionId, deviceSerial));
    return Task.CompletedTask;
  }
}
