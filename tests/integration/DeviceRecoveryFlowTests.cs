using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using GameBot.Domain.Logging;
using GameBot.Domain.Queues;
using GameBot.Service.Services.ExecutionLog;
using GameBot.Service.Services.Notifications;
using GameBot.Service.Services.QueueExecution;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

#pragma warning disable CA2007, CA2000 // test code

namespace GameBot.IntegrationTests;

/// <summary>
/// Feature 121 (user story 2, issue #261): a queue with <c>deviceRecovery</c> reboots its instance,
/// binds a new session, runs the held firings, and sends one "device live again" message.
/// </summary>
[Collection("ConfigIsolation")]
public sealed class DeviceRecoveryFlowTests {
  public DeviceRecoveryFlowTests() {
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

  [Fact]
  public async Task ABlockedDeviceIsRebootedOnceTheHeldFiringRunsAndOneLiveAgainMessageArrives() {
    var devices = new SimulatedDevices();
    devices.Block(1);
    var control = new SimulatedEmulatorControl(devices);
    var dispatcher = new AlertCapturingDispatcher();
    using var app = DeviceRecoveryHost.Create(devices, control, dispatcher);
    var client = NewClient(app);
    var sequenceId = "seq-" + Guid.NewGuid().ToString("N");
    var id = await DeviceRecoveryHost.CreateQueueAsync(app, 1, Reboot, sequenceId);

    (await StartAsync(client, id)).StatusCode.Should().Be(HttpStatusCode.OK);
    try {
      await DeviceRecoveryHost.WaitForAsync(() => dispatcher.Count(QueueAlertKind.LiveAgain) >= 1);

      control.Reboots.Should().Be(1);
      control.Starts.Single().Name.Should().Be("LDPlayer-1");
      dispatcher.Count(QueueAlertKind.NotLive).Should().Be(1);
      dispatcher.Count(QueueAlertKind.LiveAgain).Should().Be(1);
      dispatcher.Count(QueueAlertKind.RecoveryFailed).Should().Be(0);

      // The held firing runs after the recovery: a sequence entry that is not the held entry exists.
      var log = app.Services.GetRequiredService<IExecutionLogService>();
      await DeviceRecoveryHost.WaitForAsync(() => {
        var page = log.QueryAsync(new ExecutionLogQuery { ObjectType = "sequence", ObjectId = sequenceId, PageSize = 50 }).GetAwaiter().GetResult();
        return page.Items.Any(e => e.Summary != "device_not_live: capture_stalled");
      });
      var entries = await log.QueryAsync(new ExecutionLogQuery { ObjectType = "sequence", ObjectId = sequenceId, PageSize = 50 });
      entries.Items.Should().Contain(e => e.Summary == "device_not_live: capture_stalled", "the firing was held first");
      entries.Items.Should().Contain(e => e.Summary != "device_not_live: capture_stalled", "the held firing ran after the recovery");
      // The once-per-run entry ran, so the non-cycling run can already be over. No health read here.
    }
    finally {
      await StopAsync(client, id);
    }
  }

  [Fact]
  public async Task AStopDuringTheRebootFreesTheSlot() {
    var devices = new SimulatedDevices();
    devices.Block(1);
    var control = new SimulatedEmulatorControl(devices) { Hang = true };
    var dispatcher = new AlertCapturingDispatcher();
    using var app = DeviceRecoveryHost.Create(devices, control, dispatcher);
    var client = NewClient(app);
    var id = await DeviceRecoveryHost.CreateQueueAsync(app, 1, Reboot);
    (await StartAsync(client, id)).StatusCode.Should().Be(HttpStatusCode.OK);
    await DeviceRecoveryHost.WaitForAsync(() => control.Reboots == 1);
    var runState = (await DetailAsync(client, id)).GetProperty("health").GetProperty("deviceLiveness").GetProperty("recoveryState").GetString();
    runState.Should().Be("running");

    var stop = await StopAsync(client, id);

    ((int)stop.StatusCode).Should().BeLessThan(500);
    var svc = app.Services.GetRequiredService<IQueueExecutionService>();
    await DeviceRecoveryHost.WaitForAsync(() => !svc.IsRunning(id), 10000);
    svc.IsRunning(id).Should().BeFalse();

    // A second queue on another device can use the slot at once.
    control.Hang = false;
    devices.Block(2);
    var id2 = await DeviceRecoveryHost.CreateQueueAsync(app, 2, Reboot);
    (await StartAsync(client, id2)).StatusCode.Should().Be(HttpStatusCode.OK);
    try {
      await DeviceRecoveryHost.WaitForAsync(() => dispatcher.Count(QueueAlertKind.LiveAgain, id2) >= 1);
      dispatcher.Count(QueueAlertKind.LiveAgain, id2).Should().Be(1);
    }
    finally {
      await StopAsync(client, id2);
    }
  }

  [Fact]
  public async Task TwoQueuesOnOneInstanceShareOneRebootAndEachSendsItsOwnMessages() {
    var devices = new SimulatedDevices();
    devices.Block(1);
    var control = new SimulatedEmulatorControl(devices) { Duration = TimeSpan.FromMilliseconds(600) };
    var dispatcher = new AlertCapturingDispatcher();
    using var app = DeviceRecoveryHost.Create(devices, control, dispatcher);
    var client = NewClient(app);
    var idA = await DeviceRecoveryHost.CreateQueueAsync(app, 1, Reboot);
    // A second queue on the same device and instance is refused by the device claim, so the second
    // queue uses another serial that the same instance name unblocks.
    var queues = app.Services.GetRequiredService<IQueueRepository>();
    var second = await queues.GetAsync(idA);
    var otherSerial = "emu-1b";
    devices.Block(otherSerial, second!.EmulatorInstanceName!);
    var templates = app.Services.GetRequiredService<GameBot.Domain.QueueTemplates.IQueueTemplateRepository>();
    var template = await templates.CreateAsync(new GameBot.Domain.QueueTemplates.QueueTemplate {
      Id = "tpl-" + Guid.NewGuid().ToString("N"), Name = "Recovery b " + Guid.NewGuid().ToString("N"),
      Entries = { new GameBot.Domain.QueueTemplates.QueueTemplateEntry { SequenceId = "seq-b", ScheduleType = GameBot.Domain.QueueTemplates.ScheduleType.OncePerRun } }
    });
    var queueB = await queues.CreateAsync(new ExecutionQueue {
      Id = "q-" + Guid.NewGuid().ToString("N"), Name = "Recovery 1b", EmulatorSerial = otherSerial,
      EmulatorInstanceName = second.EmulatorInstanceName, LinkedTemplateId = template.Id, DeviceRecovery = Reboot
    });

    (await StartAsync(client, idA)).StatusCode.Should().Be(HttpStatusCode.OK);
    (await StartAsync(client, queueB.Id)).StatusCode.Should().Be(HttpStatusCode.OK);
    try {
      await DeviceRecoveryHost.WaitForAsync(() =>
        dispatcher.Count(QueueAlertKind.LiveAgain, idA) >= 1 && dispatcher.Count(QueueAlertKind.LiveAgain, queueB.Id) >= 1);

      control.Reboots.Should().Be(1, "queues of one instance share one reboot");
      dispatcher.Count(QueueAlertKind.NotLive, idA).Should().Be(1);
      dispatcher.Count(QueueAlertKind.NotLive, queueB.Id).Should().Be(1);
      dispatcher.Count(QueueAlertKind.LiveAgain, idA).Should().Be(1);
      dispatcher.Count(QueueAlertKind.LiveAgain, queueB.Id).Should().Be(1);
    }
    finally {
      await StopAsync(client, idA);
      await StopAsync(client, queueB.Id);
    }
  }
}
