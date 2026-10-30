using System;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using GameBot.Domain.Queues;
using GameBot.Service.Services.Notifications;
using Xunit;

#pragma warning disable CA2007, CA1861, CA1859, CA1707

// The tests use the queue test harness (a partial class), so they share its namespace.
namespace GameBot.UnitTests.Queues;

/// <summary>
/// Feature 170: the time limit still cancels the step that is in progress. Only the final status changes.
/// </summary>
public sealed partial class QueueExecutionServiceTests {
  [Theory]
  [InlineData("end", SequenceRunStatus.Success, true)]
  [InlineData("fail", SequenceRunStatus.Cancelled, false)]
  public async Task InFlightStep_IsCancelledAtTheLimitAndOnlyTheStatusDiffers(
      string onTimeLimit, SequenceRunStatus expectedStatus, bool expectSuccessMessage) {
    var expectedNotification = expectSuccessMessage ? NotificationRunStatus.Success : NotificationRunStatus.Failure;
    var h = new Harness(sequenceRepository: true);
    await AddLimitedSequenceAsync(h, "A", onTimeLimit);
    h.AddQueue("q1", new[] { "A" });
    var stepCancelled = false;
    var stepFinishedItsWork = false;
    h.Sequences.Handler = async (id, ct) => {
      try {
        await Task.Delay(Timeout.Infinite, ct);
        stepFinishedItsWork = true;
      }
      catch (OperationCanceledException) {
        stepCancelled = true;
        throw;
      }
      return FakeSequenceExecution.Success(id);
    };
    var store = new InMemoryRunStatisticsStore();
    var dispatcher = new RecordingDispatcher();
    var service = NotifyService(h, dispatcher, store: store);

    await service.StartAsync("q1");
    await WaitUntilStoppedAsync(service, "q1");

    stepCancelled.Should().BeTrue("the limit cancels the step in progress");
    stepFinishedItsWork.Should().BeFalse();
    SingleStatus(store, "A").Should().Be(expectedStatus);
    dispatcher.Jobs.Should().ContainSingle().Which.Status.Should().Be(expectedNotification);
  }
}
