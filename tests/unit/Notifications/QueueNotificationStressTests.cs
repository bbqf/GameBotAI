using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using GameBot.Domain.Notifications;
using GameBot.Service.Services.Notifications;
using Xunit;

#pragma warning disable CA2007, CA1861, CA1859, CA1707

namespace GameBot.UnitTests.Notifications;

/// <summary>
/// Feature 120 (V-16, FR-023): 8 threads write jobs, level changes to None and deletes for many
/// rounds. The streak state guard throws when two threads use the state at the same time.
/// </summary>
public sealed class QueueNotificationStressTests {
  [Fact]
  public async Task V16_ConcurrentJobsResetsAndDeletesNeverBreakTheStateOwner() {
    const int repeats = 50;
    const int rounds = 2000;
    const int threads = 8;

    for (var repeat = 0; repeat < repeats; repeat++) {
      await using var h = new NotificationHarness(NotificationLevel.Failure);
      h.Channel.Behavior = (_, _, _) => Task.FromResult(NotificationSendResult.Ok());
      await h.Worker.StartAsync(CancellationToken.None);
      var faults = new List<Exception>();

      var workers = Enumerable.Range(0, threads).Select(t => Task.Run(() => {
        try {
          for (var i = 0; i < rounds / threads; i++) {
            switch ((i + t) % 3) {
              case 0:
                h.Dispatcher.Enqueue(NotificationHarness.Job(NotificationRunStatus.Failure, sequenceId: $"s{i % 5}"));
                break;
              case 1:
                h.Dispatcher.ResetStreaks("q1");
                break;
              default:
                h.Dispatcher.Enqueue(NotificationHarness.Job(NotificationRunStatus.Success, sequenceId: $"s{i % 5}"));
                break;
            }
          }
        }
        catch (Exception ex) {
          lock (faults) faults.Add(ex);
        }
      })).ToArray();
      await Task.WhenAll(workers);

      // The last item is a delete reset. After it, the queue has no streak.
      h.Dispatcher.ResetStreaks("q1");
      await NotificationHarness.WaitForAsync(() => h.Dispatcher.QueuedRunJobs == 0);
      await Task.Delay(20);
      // A last reset item and a short wait make sure that all earlier jobs ended before we read the state.
      h.Dispatcher.ResetStreaks("q1");
      await Task.Delay(50);

      faults.Should().BeEmpty();
      h.WorkerLog.Lines.Should().NotContain(l => l.Contains("could not handle", StringComparison.Ordinal));
      h.Worker.Streaks.OpenCount("q1").Should().Be(0);
    }
  }
}
