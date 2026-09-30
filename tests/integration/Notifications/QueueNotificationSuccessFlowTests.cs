using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using GameBot.Domain.Notifications;
using GameBot.Service.Services.Notifications;
using GameBot.UnitTests.Notifications;
using Xunit;

#pragma warning disable CA2007, CA1861, CA1707, CA2000

namespace GameBot.IntegrationTests.Notifications;

/// <summary>
/// Feature 120 (V-03, SC-003): the message count and the circle for all 3 levels and 4 results,
/// through the real dispatcher, the real worker and the real queue repository.
/// </summary>
public sealed class QueueNotificationSuccessFlowTests {
  private const string Green = "\U0001F7E2";
  private const string Red = "\U0001F534";
  private const string Yellow = "\U0001F7E1";

  public static IEnumerable<object[]> Cases() {
    // level, results in order, expected circle+word for each message
    yield return new object[] { NotificationLevel.None, "success", Array.Empty<string>() };
    yield return new object[] { NotificationLevel.None, "failure", Array.Empty<string>() };
    yield return new object[] { NotificationLevel.None, "cancelled", Array.Empty<string>() };
    yield return new object[] { NotificationLevel.None, "failure,success", Array.Empty<string>() };
    yield return new object[] { NotificationLevel.Failure, "success", Array.Empty<string>() };
    yield return new object[] { NotificationLevel.Failure, "failure", new[] { $"{Red} failure" } };
    yield return new object[] { NotificationLevel.Failure, "cancelled", new[] { $"{Yellow} cancelled" } };
    yield return new object[] { NotificationLevel.Failure, "failure,success", new[] { $"{Red} failure", $"{Green} recovered" } };
    yield return new object[] { NotificationLevel.SuccessAndFailure, "success", new[] { $"{Green} success" } };
    yield return new object[] { NotificationLevel.SuccessAndFailure, "failure", new[] { $"{Red} failure" } };
    yield return new object[] { NotificationLevel.SuccessAndFailure, "cancelled", new[] { $"{Yellow} cancelled" } };
    yield return new object[] { NotificationLevel.SuccessAndFailure, "failure,success", new[] { $"{Red} failure", $"{Green} recovered" } };
  }

  [Theory]
  [MemberData(nameof(Cases))]
  public async Task V03_TheMessageCountAndTheCircleMatchTheTable(NotificationLevel level, string results, string[] expected) {
    ArgumentNullException.ThrowIfNull(results);
    ArgumentNullException.ThrowIfNull(expected);
    await using var h = new NotificationHarness(level);
    await h.Worker.StartAsync(System.Threading.CancellationToken.None);

    foreach (var result in results.Split(',')) {
      var status = result switch { "success" => NotificationRunStatus.Success, "failure" => NotificationRunStatus.Failure, _ => NotificationRunStatus.Cancelled };
      h.Dispatcher.Enqueue(NotificationHarness.Job(status));
      await NotificationHarness.WaitForAsync(() => h.Dispatcher.QueuedRunJobs == 0);
      await h.WaitIdleAsync();
    }

    await Task.Delay(50);
    h.Channel.Sent.Select(m => m.Text).Should().Equal(expected.Select(e => $"Farm-1 : PNS.Collect : {e}"));
  }
}
