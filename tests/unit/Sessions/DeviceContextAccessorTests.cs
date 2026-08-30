using System;
using System.Threading.Tasks;
using FluentAssertions;
using GameBot.Domain.Sessions;
using Xunit;

#pragma warning disable CA2007, CA1861, CA1859

namespace GameBot.UnitTests.Sessions;

/// <summary>
/// Feature 079: the ambient device context must flow with the execution context so a queue run's
/// device is visible to everything it starts, and must not leak between sibling flows.
/// </summary>
public sealed class DeviceContextAccessorTests {
  [Fact]
  public void CurrentIsNullBeforeAnyPush() {
    var accessor = new AsyncLocalDeviceContextAccessor();
    accessor.Current.Should().BeNull();
  }

  [Fact]
  public void PushSetsCurrentAndDisposeRestoresIt() {
    var accessor = new AsyncLocalDeviceContextAccessor();

    using (accessor.Push(DeviceContext.For("s1", "emulator-5558"))) {
      accessor.Current!.SessionId.Should().Be("s1");
      accessor.Current!.DeviceSerial.Should().Be("emulator-5558");
    }

    accessor.Current.Should().BeNull();
  }

  [Fact]
  public void NestedPushesRestoreTheOuterContext() {
    var accessor = new AsyncLocalDeviceContextAccessor();

    using (accessor.Push(DeviceContext.For("outer"))) {
      using (accessor.Push(DeviceContext.For("inner"))) {
        accessor.Current!.SessionId.Should().Be("inner");
      }
      accessor.Current!.SessionId.Should().Be("outer");
    }

    accessor.Current.Should().BeNull();
  }

  [Fact]
  public void DisposingTwiceIsANoOp() {
    var accessor = new AsyncLocalDeviceContextAccessor();
    var scope = accessor.Push(DeviceContext.For("s1"));
    scope.Dispose();
    scope.Dispose();
    accessor.Current.Should().BeNull();
  }

  [Fact]
  public async Task ContextFlowsAcrossAwait() {
    var accessor = new AsyncLocalDeviceContextAccessor();

    using (accessor.Push(DeviceContext.For("s1"))) {
      await Task.Yield();
      await Task.Delay(1).ConfigureAwait(false);
      accessor.Current!.SessionId.Should().Be("s1");
    }
  }

  [Fact]
  public async Task ContextFlowsIntoTaskRun() {
    var accessor = new AsyncLocalDeviceContextAccessor();

    using (accessor.Push(DeviceContext.For("s1"))) {
      var observed = await Task.Run(() => accessor.Current?.SessionId).ConfigureAwait(false);
      observed.Should().Be("s1");
    }
  }

  [Fact]
  public async Task AChildFlowsPushDoesNotLeakToTheParent() {
    var accessor = new AsyncLocalDeviceContextAccessor();

    await Task.Run(() => {
      using var scope = accessor.Push(DeviceContext.For("child"));
      accessor.Current!.SessionId.Should().Be("child");
    }).ConfigureAwait(false);

    accessor.Current.Should().BeNull();
  }

  [Fact]
  public async Task ConcurrentFlowsSeeTheirOwnContext() {
    var accessor = new AsyncLocalDeviceContextAccessor();

    async Task<string?> RunWith(string sessionId) {
      using var scope = accessor.Push(DeviceContext.For(sessionId));
      await Task.Delay(5).ConfigureAwait(false);
      return accessor.Current?.SessionId;
    }

    var results = await Task.WhenAll(RunWith("a"), RunWith("b"), RunWith("c")).ConfigureAwait(false);

    results.Should().BeEquivalentTo(new[] { "a", "b", "c" });
    accessor.Current.Should().BeNull();
  }

  [Fact]
  public void PushRejectsNull() {
    var accessor = new AsyncLocalDeviceContextAccessor();
    var act = () => accessor.Push(null!);
    act.Should().Throw<ArgumentNullException>();
  }

  [Theory]
  [InlineData("")]
  [InlineData("   ")]
  [InlineData(null)]
  public void ForRejectsABlankSessionId(string? sessionId) {
    var act = () => DeviceContext.For(sessionId!);
    act.Should().Throw<ArgumentException>();
  }

  [Fact]
  public void ForNormalizesABlankSerialToNull() {
    DeviceContext.For("s1", "   ").DeviceSerial.Should().BeNull();
  }
}
