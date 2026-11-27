using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using GameBot.Domain.Logging;
using GameBot.Domain.Queues;
using GameBot.Domain.Sessions;
using GameBot.Emulator.Session;
using GameBot.Service.Services.ExecutionLog;
using GameBot.Service.Services.Notifications;
using GameBot.Service.Services.QueueExecution;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

#pragma warning disable CA2007, CA2000 // test code

namespace GameBot.IntegrationTests;

/// <summary>
/// Feature 201 (B-047, issue #416): a recovery reboot removes the session of the queue, and ADB does not list
/// the device for some time. A queue with <c>reboot-instance</c> stays Running, holds its firings, and binds a
/// new session when the device is listed again. A queue with recovery off ends its run as before.
/// </summary>
[Collection("ConfigIsolation")]
public sealed class DeviceRecoveryLostSessionTests {
  private const string Serial = "emu-1";

  public DeviceRecoveryLostSessionTests() {
    Environment.SetEnvironmentVariable("GAMEBOT_USE_ADB", "false");
    Environment.SetEnvironmentVariable("GAMEBOT_AUTH_TOKEN", "test-token");
    TestEnvironment.PrepareCleanDataDir();
  }

  private static QueueDeviceRecovery Reboot(int maxAttempts) => new() {
    Action = QueueDeviceRecovery.ActionRebootInstance, AfterMs = 1000, MaxAttempts = maxAttempts, CooldownMs = 0
  };

  private static HttpClient NewClient(WebApplicationFactory<Program> app) {
    var client = app.CreateClient();
    client.DefaultRequestHeaders.Add("Authorization", "Bearer test-token");
    return client;
  }

  private static Task<HttpResponseMessage> StartAsync(HttpClient client, string id) =>
    client.PostAsync(new Uri($"/api/queues/{id}/start", UriKind.Relative), null);

  private static Task<HttpResponseMessage> StopAsync(HttpClient client, string id) =>
    client.PostAsync(new Uri($"/api/queues/{id}/stop", UriKind.Relative), null);

  private static async Task<JsonElement> DetailAsync(HttpClient client, string id) {
    var resp = await client.GetAsync(new Uri($"/api/queues/{id}", UriKind.Relative));
    var body = await resp.Content.ReadAsStringAsync();
    resp.StatusCode.Should().Be(HttpStatusCode.OK, body);
    return DeviceRecoveryHost.Parse(body);
  }

  private static string? Status(JsonElement detail) => detail.GetProperty("status").GetString();

  private static JsonElement? Liveness(JsonElement detail) =>
    detail.TryGetProperty("health", out var health) && health.ValueKind == JsonValueKind.Object
    && health.TryGetProperty("deviceLiveness", out var liveness) && liveness.ValueKind == JsonValueKind.Object
      ? liveness
      : null;

  // A reboot drops the ADB transport: ADB does not list the serial, and the session of the serial is gone.
  // The old session is removed at the start of the reboot, so no check sees it live after the reboot.
  private static void DropTransport(WebApplicationFactory<Program> app, SimulatedDevices devices) {
    devices.Unlist(Serial);
    var sessions = app.Services.GetRequiredService<ISessionManager>();
    foreach (var session in sessions.ListSessions().Where(s => s.DeviceSerial == Serial).ToList()) {
      sessions.StopSession(session.Id);
    }
  }

  private static int SessionsOf(WebApplicationFactory<Program> app) =>
    app.Services.GetRequiredService<ISessionManager>().ListSessions().Count(s => s.DeviceSerial == Serial);

  // Waits until the queue is Running with no session and shows the reason transport_not_ready.
  private static Task WaitForNoSessionHoldAsync(HttpClient client, string id) =>
    DeviceRecoveryHost.WaitForAsync(() => {
      // A live state has no reason member, and one failed read must not end the wait.
      using var resp = client.GetAsync(new Uri($"/api/queues/{id}", UriKind.Relative)).GetAwaiter().GetResult();
      if (resp.StatusCode != HttpStatusCode.OK) return false;
      var detail = DeviceRecoveryHost.Parse(resp.Content.ReadAsStringAsync().GetAwaiter().GetResult());
      return Status(detail) == "Running"
        && Liveness(detail) is { } liveness
        && liveness.TryGetProperty("reason", out var reason)
        && reason.ValueKind == JsonValueKind.String
        && reason.GetString() == DeviceLivenessReasons.TransportNotReady;
    }, 20000, "the hold of the queue with no session");

  private static async Task<string?> QueueRootSummaryAsync(WebApplicationFactory<Program> app, string id) {
    var log = app.Services.GetRequiredService<IExecutionLogService>();
    var page = await log.QueryAsync(new ExecutionLogQuery { ObjectType = "queue", RootsOnly = true, PageSize = 100 });
    return page.Items.Where(e => e.ObjectRef.ObjectId == id).Select(e => e.Summary).FirstOrDefault(s => s is not null && s.Contains("failed", StringComparison.Ordinal));
  }

  [Fact]
  public async Task ARecoveryRebootThatRemovesTheSessionKeepsTheQueueRunningAndRunsTheHeldFiring() {
    var devices = new SimulatedDevices();
    devices.Block(1);
    var control = new SimulatedEmulatorControl(devices);
    var dispatcher = new AlertCapturingDispatcher();
    using var app = DeviceRecoveryHost.Create(devices, control, dispatcher);
    var unlistedOnce = 0;
    control.OnReboot = _ => {
      if (System.Threading.Interlocked.Exchange(ref unlistedOnce, 1) == 0) DropTransport(app, devices);
    };
    var client = NewClient(app);
    var sequenceId = "seq-" + Guid.NewGuid().ToString("N");
    var id = await DeviceRecoveryHost.CreateQueueAsync(app, 1, Reboot(maxAttempts: 1), sequenceId, keepAlive: true);

    (await StartAsync(client, id)).StatusCode.Should().Be(HttpStatusCode.OK);
    try {
      await WaitForNoSessionHoldAsync(client, id);
      SessionsOf(app).Should().Be(0, "the rebind of the recovery removed the session, and ADB does not list the device");

      // FR-001: the queue keeps its run and its device claim.
      (await StartAsync(client, id)).StatusCode.Should().Be(HttpStatusCode.Conflict);

      devices.List(Serial);

      var log = app.Services.GetRequiredService<IExecutionLogService>();
      await DeviceRecoveryHost.WaitForAsync(() => {
        var page = log.QueryAsync(new ExecutionLogQuery { ObjectType = "sequence", ObjectId = sequenceId, PageSize = 50 }).GetAwaiter().GetResult();
        return page.Items.Any(e => e.Summary is null || !e.Summary.StartsWith("device_not_live:", StringComparison.Ordinal));
      }, 20000, "the run of the held firing");
      SessionsOf(app).Should().Be(1, "the queue binds one new session after the device is listed again");
      Status(await DetailAsync(client, id)).Should().Be("Running");
      (await QueueRootSummaryAsync(app, id)).Should().BeNull("the run did not end with 'emulator connection lost mid-run'");
    }
    finally {
      await StopAsync(client, id);
    }
  }

  [Fact]
  public async Task AQueueWithRecoveryOffStillStopsWhenTheSessionIsLost() {
    var devices = new SimulatedDevices();
    devices.Block(1);
    var control = new SimulatedEmulatorControl(devices);
    var dispatcher = new AlertCapturingDispatcher();
    using var app = DeviceRecoveryHost.Create(devices, control, dispatcher);
    var client = NewClient(app);
    var id = await DeviceRecoveryHost.CreateQueueAsync(app, 1, new QueueDeviceRecovery { Action = QueueDeviceRecovery.ActionNone, AfterMs = 60000 });
    var svc = app.Services.GetRequiredService<IQueueExecutionService>();
    var sessions = app.Services.GetRequiredService<ISessionManager>();

    (await StartAsync(client, id)).StatusCode.Should().Be(HttpStatusCode.OK);
    try {
      await DeviceRecoveryHost.WaitForAsync(() => SessionsOf(app) == 1, what: "the session of the queue");
      devices.Unlist(Serial);
      sessions.StopSession(sessions.ListSessions().Single(s => s.DeviceSerial == Serial).Id);

      await DeviceRecoveryHost.WaitForAsync(() => !svc.IsRunning(id), 20000, "the end of the run");

      (await QueueRootSummaryAsync(app, id)).Should().Contain($"emulator connection lost mid-run ('{Serial}')");
      control.Reboots.Should().Be(0);
    }
    finally {
      await StopAsync(client, id);
    }
  }

  [Fact]
  public async Task AStopWhileTheQueueWaitsForItsDeviceEndsTheRun() {
    var devices = new SimulatedDevices();
    devices.Block(1);
    var control = new SimulatedEmulatorControl(devices);
    var dispatcher = new AlertCapturingDispatcher();
    using var app = DeviceRecoveryHost.Create(devices, control, dispatcher);
    control.OnReboot = _ => DropTransport(app, devices);
    var client = NewClient(app);
    var id = await DeviceRecoveryHost.CreateQueueAsync(app, 1, Reboot(maxAttempts: 1), keepAlive: true);
    var svc = app.Services.GetRequiredService<IQueueExecutionService>();

    (await StartAsync(client, id)).StatusCode.Should().Be(HttpStatusCode.OK);
    await WaitForNoSessionHoldAsync(client, id);

    (await StopAsync(client, id)).StatusCode.Should().Be(HttpStatusCode.OK);
    await DeviceRecoveryHost.WaitForAsync(() => !svc.IsRunning(id), 10000, "the end of the stopped run");

    Status(await DetailAsync(client, id)).Should().Be("Stopped");
    SessionsOf(app).Should().Be(0);
    devices.List(Serial);
    // The device claim is free: a new start of the queue is accepted.
    (await StartAsync(client, id)).StatusCode.Should().Be(HttpStatusCode.OK);
    await StopAsync(client, id);
  }

  [Fact]
  public async Task AQueueThatCannotBindAfterAllAttemptsSendsOneRecoveryFailedAlertAndStaysRunning() {
    var devices = new SimulatedDevices();
    devices.Block(1);
    var control = new SimulatedEmulatorControl(devices);
    var dispatcher = new AlertCapturingDispatcher();
    using var app = DeviceRecoveryHost.Create(devices, control, dispatcher);
    control.OnReboot = _ => DropTransport(app, devices);
    var client = NewClient(app);
    var id = await DeviceRecoveryHost.CreateQueueAsync(app, 1, Reboot(maxAttempts: 2), keepAlive: true);

    (await StartAsync(client, id)).StatusCode.Should().Be(HttpStatusCode.OK);
    try {
      await DeviceRecoveryHost.WaitForAsync(() => dispatcher.Count(QueueAlertKind.RecoveryFailed, id) >= 1, 20000, "the 'recovery failed' alert");
      await Task.Delay(300);

      dispatcher.Count(QueueAlertKind.RecoveryFailed, id).Should().Be(1);
      control.Reboots.Should().Be(2);
      var detail = await DetailAsync(client, id);
      Status(detail).Should().Be("Running");
      Liveness(detail)!.Value.GetProperty("recoveryState").GetString().Should().Be("exhausted");

      devices.List(Serial);

      await DeviceRecoveryHost.WaitForAsync(() => dispatcher.Count(QueueAlertKind.LiveAgain, id) >= 1, 20000, "the 'live again' message");
      SessionsOf(app).Should().Be(1);
      dispatcher.Count(QueueAlertKind.LiveAgain, id).Should().Be(1);
    }
    finally {
      await StopAsync(client, id);
    }
  }
}
