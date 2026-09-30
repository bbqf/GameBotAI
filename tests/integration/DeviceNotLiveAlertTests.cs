using System;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using GameBot.Service.Services.Notifications;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

#pragma warning disable CA2007, CA2000 // test code

namespace GameBot.IntegrationTests;

/// <summary>
/// Feature 121 (user story 1, issue #261): a device that stays not live gives one alert and, after it is
/// live again, one "live again" message. The queue has no failure policy and no recovery.
/// </summary>
[Collection("ConfigIsolation")]
public sealed class DeviceNotLiveAlertTests {
  public DeviceNotLiveAlertTests() {
    Environment.SetEnvironmentVariable("GAMEBOT_USE_ADB", "false");
    Environment.SetEnvironmentVariable("GAMEBOT_AUTH_TOKEN", "test-token");
    TestEnvironment.PrepareCleanDataDir();
  }

  private static HttpClient NewClient(WebApplicationFactory<Program> app) {
    var client = app.CreateClient();
    client.DefaultRequestHeaders.Add("Authorization", "Bearer test-token");
    return client;
  }

  [Fact]
  public async Task ABlockedDeviceGivesOneAlertThenOneLiveAgainMessage() {
    var devices = new SimulatedDevices();
    devices.Block(1);
    var control = new SimulatedEmulatorControl(devices);
    var dispatcher = new AlertCapturingDispatcher();
    using var app = DeviceRecoveryHost.Create(devices, control, dispatcher, alertAfterMs: 1000);
    var client = NewClient(app);
    var id = await DeviceRecoveryHost.CreateQueueAsync(app, 1, recovery: null);

    (await client.PostAsync(new Uri($"/api/queues/{id}/start", UriKind.Relative), null)).StatusCode.Should().Be(HttpStatusCode.OK);
    try {
      await DeviceRecoveryHost.WaitForAsync(() => dispatcher.Count(QueueAlertKind.NotLive) >= 1);
      dispatcher.Count(QueueAlertKind.NotLive).Should().Be(1);

      var health = await HealthAsync(client, id);
      health.GetProperty("lastNotificationAt").ValueKind.Should().Be(JsonValueKind.String);
      health.GetProperty("lastNotificationSucceeded").GetBoolean().Should().BeTrue();
      var liveness = health.GetProperty("deviceLiveness");
      liveness.GetProperty("alertSent").GetBoolean().Should().BeTrue();
      liveness.GetProperty("recoveryState").GetString().Should().Be("idle");

      // No second alert while the device stays not live.
      await Task.Delay(1500);
      dispatcher.Count(QueueAlertKind.NotLive).Should().Be(1);
      control.Reboots.Should().Be(0, "a queue with no deviceRecovery never reboots");

      devices.UnblockInstance(SimulatedDevices.InstanceOf(1));
      await DeviceRecoveryHost.WaitForAsync(() => dispatcher.Count(QueueAlertKind.LiveAgain) >= 1);
      await Task.Delay(500);

      dispatcher.Count(QueueAlertKind.LiveAgain).Should().Be(1);
      dispatcher.Count(QueueAlertKind.NotLive).Should().Be(1);
      dispatcher.Alerts.Should().HaveCount(2);
    }
    finally {
      await client.PostAsync(new Uri($"/api/queues/{id}/stop", UriKind.Relative), null);
    }
  }

  [Fact]
  public async Task ADeviceThatIsLiveBeforeTheAlertTimeGivesNoMessage() {
    var devices = new SimulatedDevices();
    devices.Block(1);
    var control = new SimulatedEmulatorControl(devices);
    var dispatcher = new AlertCapturingDispatcher();
    using var app = DeviceRecoveryHost.Create(devices, control, dispatcher, alertAfterMs: 600000);
    var client = NewClient(app);
    var id = await DeviceRecoveryHost.CreateQueueAsync(app, 1, recovery: null);

    (await client.PostAsync(new Uri($"/api/queues/{id}/start", UriKind.Relative), null)).StatusCode.Should().Be(HttpStatusCode.OK);
    try {
      await Task.Delay(300);
      devices.UnblockInstance(SimulatedDevices.InstanceOf(1));
      await Task.Delay(500);

      dispatcher.Alerts.Should().BeEmpty();
    }
    finally {
      await client.PostAsync(new Uri($"/api/queues/{id}/stop", UriKind.Relative), null);
    }
  }

  private static async Task<JsonElement> HealthAsync(HttpClient client, string id) {
    var resp = await client.GetAsync(new Uri($"/api/queues/{id}", UriKind.Relative));
    var body = await resp.Content.ReadAsStringAsync();
    resp.StatusCode.Should().Be(HttpStatusCode.OK, body);
    return DeviceRecoveryHost.Parse(body).GetProperty("health");
  }
}
