using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using GameBot.UnitTests.Notifications;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

#pragma warning disable CA2007, CA2000, CA1707

namespace GameBot.IntegrationTests.Notifications;

/// <summary>
/// Issue #335: a <c>notify</c> step with no <c>url</c> sends its message to each enabled notification
/// target. The test uses the real dispatcher and worker and a fake channel.
/// </summary>
[Collection("ConfigIsolation")]
public sealed class NotifyStepToTargetsTests {
  public NotifyStepToTargetsTests() {
    Environment.SetEnvironmentVariable("GAMEBOT_USE_ADB", "false");
    Environment.SetEnvironmentVariable("GAMEBOT_AUTH_TOKEN", "test-token");
    TestEnvironment.PrepareCleanDataDir();
  }

  private static async Task<string> IdOfAsync(HttpResponseMessage resp) {
    resp.StatusCode.Should().Be(HttpStatusCode.Created);
    return JsonDocument.Parse(await resp.Content.ReadAsStringAsync()).RootElement.GetProperty("id").GetString()!;
  }

  [Fact]
  public async Task NotifyStepWithoutUrl_SendsTheMessageToEachEnabledTargetOnce() {
    var channel = new RecordingChannel();
    using var app = NotificationIntegrationHelpers.HostWithChannel(channel);
    using var client = app.CreateClient();
    client.DefaultRequestHeaders.Add("Authorization", "Bearer test-token");
    NotificationIntegrationHelpers.AddTarget(app.Services, "Phone");
    NotificationIntegrationHelpers.AddTarget(app.Services, "Tablet");
    const string message = "Guard found an unusable screen";

    var sequenceId = await IdOfAsync(await client.PostAsJsonAsync("/api/sequences", new {
      name = "Guard-" + Guid.NewGuid().ToString("N"),
      version = 1,
      steps = new object[] {
        new {
          stepId = "alert",
          label = "Raise an alert",
          stepType = "Action",
          primitiveAction = new { type = "notify", schemaVersion = "1", payload = new { message } }
        }
      }
    }));
    var queueId = await IdOfAsync(await client.PostAsJsonAsync(new Uri("/api/queues", UriKind.Relative), new {
      name = "Guarded farm", emulatorSerial = "emu-offline", cycleExecution = false
    }));
    var templateId = await IdOfAsync(await client.PostAsJsonAsync(new Uri("/api/queue-templates", UriKind.Relative), new {
      name = "Tpl-" + Guid.NewGuid().ToString("N"),
      entries = new[] { new { sequenceId, scheduleType = "OncePerRun", timerTimeOfDay = (string?)null } },
      overwrite = false
    }));
    (await client.PutAsJsonAsync(new Uri($"/api/queues/{queueId}/template", UriKind.Relative), new { templateId }))
      .EnsureSuccessStatusCode();
    (await client.PostAsync(new Uri($"/api/queues/{queueId}/start", UriKind.Relative), null)).EnsureSuccessStatusCode();

    await NotificationIntegrationHelpers.WaitForAsync(() => channel.Sent.Count(s => s.Text == message) >= 2, 20000);
    await Task.Delay(300);

    channel.Sent.Where(s => s.Text == message).Should().HaveCount(2);
  }
}
