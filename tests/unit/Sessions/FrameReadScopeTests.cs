#pragma warning disable CA2007 // test code: no ConfigureAwait
using System;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using GameBot.Domain.Sessions;
using GameBot.UnitTests.Queues;
using Xunit;

namespace GameBot.UnitTests.Sessions;

/// <summary>Feature 197 (research R-004): the frame freshness gate of a sequence run.</summary>
public sealed class FrameReadScopeTests {
  private static readonly DateTimeOffset T0 = new(2026, 11, 20, 12, 0, 0, TimeSpan.Zero);

  [Fact]
  public void WithNoScopeAdmitIsTrueAndRecordsNothing() {
    using var bmp = new Bitmap(1, 1);
    var old = new CachedFrame(new byte[] { 1 }, bmp, T0.AddHours(-1), 1, 1);

    FrameReadScope.Current.Should().BeNull();
    FrameReadScope.Admit(old).Should().BeTrue();
  }

  [Fact]
  public void AFreshFrameIsAdmittedAndRecorded() {
    var clock = new FakeTimeProvider(T0.AddMilliseconds(400));
    var scope = new FrameReadScope(5000, clock);
    using var bmp = new Bitmap(1, 1);
    var frame = new CachedFrame(new byte[] { 1 }, bmp, T0, 1, 1) { FrameId = "fresh" };

    using (FrameReadScope.Push(scope)) {
      FrameReadScope.Admit(frame).Should().BeTrue();
    }

    scope.LastRead.Should().Be(new FrameReadInfo("fresh", 400, false));
  }

  [Fact]
  public void AStaleFrameIsRefusedAndRecorded() {
    var clock = new FakeTimeProvider(T0.AddMilliseconds(7000));
    var scope = new FrameReadScope(5000, clock);
    using var bmp = new Bitmap(1, 1);
    var frame = new CachedFrame(new byte[] { 1 }, bmp, T0, 1, 1) { FrameId = "old" };

    using (FrameReadScope.Push(scope)) {
      FrameReadScope.Admit(frame).Should().BeFalse();
    }

    scope.LastRead!.Stale.Should().BeTrue();
    scope.LastRead.AgeMs.Should().Be(7000);
    scope.StaleMessage().Should().Be("stale_frame: the frame old is 7000 ms old (limit 5000 ms).");
  }

  [Fact]
  public void NestedPushAndDisposeRestoreTheScope() {
    var outer = new FrameReadScope(5000);
    var inner = new FrameReadScope(1000);

    using (FrameReadScope.Push(outer)) {
      using (FrameReadScope.Push(inner)) {
        FrameReadScope.Current.Should().BeSameAs(inner);
      }

      FrameReadScope.Current.Should().BeSameAs(outer);
    }

    FrameReadScope.Current.Should().BeNull();
  }

  [Fact]
  public async Task EnsureFreshWithNoDelegateIsTrue() {
    var scope = new FrameReadScope(5000);

    (await scope.EnsureFreshAsync(CancellationToken.None)).Should().BeTrue();
  }

  [Fact]
  public async Task EnsureFreshCallsTheDelegate() {
    var calls = 0;
    var scope = new FrameReadScope(5000, waitForFresh: _ => {
      calls++;
      return Task.FromResult(false);
    });

    (await scope.EnsureFreshAsync(CancellationToken.None)).Should().BeFalse();
    calls.Should().Be(1);
  }
}
