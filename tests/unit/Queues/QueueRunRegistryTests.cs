using System.Threading;
using FluentAssertions;
using GameBot.Service.Services.QueueExecution;
using Xunit;

#pragma warning disable CA2007, CA1861, CA1859

namespace GameBot.UnitTests.Queues;

/// <summary>Feature 065: the extracted active-run registry add/get/remove + per-queue isolation.</summary>
public sealed class QueueRunRegistryTests {
  private static QueueRunHandle Handle(string queueId) =>
    new() { QueueId = queueId, Cts = new CancellationTokenSource() };

  [Fact]
  public void AddThenGetReturnsTheSameHandle() {
    var registry = new QueueRunRegistry();
    var handle = Handle("q1");

    registry.TryAdd("q1", handle).Should().BeTrue();
    registry.IsRunning("q1").Should().BeTrue();
    registry.TryGet("q1", out var found).Should().BeTrue();
    found.Should().BeSameAs(handle);
  }

  [Fact]
  public void AddingTwiceForTheSameQueueIsRejected() {
    var registry = new QueueRunRegistry();
    registry.TryAdd("q1", Handle("q1")).Should().BeTrue();
    registry.TryAdd("q1", Handle("q1")).Should().BeFalse();
  }

  [Fact]
  public void RemoveReturnsHandleAndClearsRunningState() {
    var registry = new QueueRunRegistry();
    var handle = Handle("q1");
    registry.TryAdd("q1", handle);

    registry.Remove("q1", out var removed).Should().BeTrue();
    removed.Should().BeSameAs(handle);
    registry.IsRunning("q1").Should().BeFalse();
    registry.TryGet("q1", out _).Should().BeFalse();
  }

  [Fact]
  public void RunsAreIsolatedPerQueue() {
    var registry = new QueueRunRegistry();
    var h1 = Handle("q1");
    var h2 = Handle("q2");
    registry.TryAdd("q1", h1);
    registry.TryAdd("q2", h2);

    registry.TryGet("q1", out var g1).Should().BeTrue();
    registry.TryGet("q2", out var g2).Should().BeTrue();
    g1.Should().BeSameAs(h1);
    g2.Should().BeSameAs(h2);

    // Removing one leaves the other untouched.
    registry.Remove("q1", out _);
    registry.IsRunning("q1").Should().BeFalse();
    registry.IsRunning("q2").Should().BeTrue();
  }

  [Fact]
  public void GetOrRemoveOnUnknownQueueReturnsFalse() {
    var registry = new QueueRunRegistry();
    registry.TryGet("nope", out _).Should().BeFalse();
    registry.Remove("nope", out _).Should().BeFalse();
    registry.IsRunning("nope").Should().BeFalse();
  }
}
