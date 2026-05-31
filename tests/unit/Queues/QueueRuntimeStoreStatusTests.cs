using FluentAssertions;
using GameBot.Domain.Queues;
using Xunit;

namespace GameBot.UnitTests.Queues;

public sealed class QueueRuntimeStoreStatusTests {
  [Fact]
  public void DefaultStatusIsStopped() {
    var store = new QueueRuntimeStore();
    store.GetStatus("q1").Should().Be(QueueExecutionStatus.Stopped);
  }

  [Fact]
  public void SetStatusRunningIsReflected() {
    var store = new QueueRuntimeStore();
    store.SetStatus("q1", QueueExecutionStatus.Running);
    store.GetStatus("q1").Should().Be(QueueExecutionStatus.Running);
  }

  [Fact]
  public void StartIsIdempotent() {
    var store = new QueueRuntimeStore();
    store.SetStatus("q1", QueueExecutionStatus.Running);
    store.SetStatus("q1", QueueExecutionStatus.Running);
    store.GetStatus("q1").Should().Be(QueueExecutionStatus.Running);
  }

  [Fact]
  public void StopIsIdempotent() {
    var store = new QueueRuntimeStore();
    store.SetStatus("q1", QueueExecutionStatus.Running);
    store.SetStatus("q1", QueueExecutionStatus.Stopped);
    store.SetStatus("q1", QueueExecutionStatus.Stopped);
    store.GetStatus("q1").Should().Be(QueueExecutionStatus.Stopped);
  }
}
