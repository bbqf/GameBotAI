using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using FluentAssertions;
using GameBot.Domain.Queues;
using GameBot.Service.Services.Notifications;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

#pragma warning disable CA2007, CA2000 // test code

namespace GameBot.IntegrationTests;

/// <summary>
/// Feature 121 (user story 3, FR-011, FR-012, SC-003): two devices that need recovery together start
/// their reboots at least one stagger time apart.
/// </summary>
[Collection("ConfigIsolation")]
public sealed class DeviceRecoveryStaggerTests {
  private const int StaggerMs = 1500;

  public DeviceRecoveryStaggerTests() {
    Environment.SetEnvironmentVariable("GAMEBOT_USE_ADB", "false");
    Environment.SetEnvironmentVariable("GAMEBOT_AUTH_TOKEN", "test-token");
    TestEnvironment.PrepareCleanDataDir();
  }

  private static readonly QueueDeviceRecovery Reboot = new() {
    Action = QueueDeviceRecovery.ActionRebootInstance, AfterMs = 1000, MaxAttempts = 2, CooldownMs = 0
  };

  private static HttpClient NewClient(WebApplicationFactory<Program> app) {
    var client = app.CreateClient();
    client.DefaultRequestHeaders.Add("Authorization", "Bearer test-token");
    return client;
  }

  [Fact]
  public async Task TwoRebootsStartAtLeastOneStaggerTimeApart() {
    var devices = new SimulatedDevices();
    devices.Block(1);
    devices.Block(2);
    // A short reboot: the second start waits for the stagger time, not for the first reboot.
    var control = new SimulatedEmulatorControl(devices) { Duration = TimeSpan.FromMilliseconds(100) };
    var dispatcher = new AlertCapturingDispatcher();
    using var app = DeviceRecoveryHost.Create(devices, control, dispatcher, staggerMs: StaggerMs);
    var client = NewClient(app);
    var idA = await DeviceRecoveryHost.CreateQueueAsync(app, 1, Reboot);
    var idB = await DeviceRecoveryHost.CreateQueueAsync(app, 2, Reboot);

    (await client.PostAsync(new Uri($"/api/queues/{idA}/start", UriKind.Relative), null)).StatusCode.Should().Be(HttpStatusCode.OK);
    (await client.PostAsync(new Uri($"/api/queues/{idB}/start", UriKind.Relative), null)).StatusCode.Should().Be(HttpStatusCode.OK);
    try {
      await DeviceRecoveryHost.WaitForAsync(() =>
        dispatcher.Count(QueueAlertKind.LiveAgain, idA) >= 1 && dispatcher.Count(QueueAlertKind.LiveAgain, idB) >= 1, 30000);

      control.Reboots.Should().Be(2);
      var starts = control.Starts.Select(s => s.StartedAt).OrderBy(t => t).ToArray();
      (starts[1] - starts[0]).TotalMilliseconds.Should().BeGreaterThanOrEqualTo(StaggerMs - 50);
    }
    finally {
      await client.PostAsync(new Uri($"/api/queues/{idA}/stop", UriKind.Relative), null);
      await client.PostAsync(new Uri($"/api/queues/{idB}/stop", UriKind.Relative), null);
    }
  }
}
