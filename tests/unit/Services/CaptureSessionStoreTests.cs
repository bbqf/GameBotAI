using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using FluentAssertions;
using GameBot.Service.Services;
using Xunit;

#pragma warning disable CA1416

namespace GameBot.UnitTests.Services;

/// <summary>Feature 197 (FR-009): the store of the screenshot captures keeps one capture for each frame id.</summary>
public sealed class CaptureSessionStoreTests {
  private static byte[] Png(int width = 4, int height = 3) {
    using var bmp = new Bitmap(width, height);
    using var ms = new MemoryStream();
    bmp.Save(ms, ImageFormat.Png);
    return ms.ToArray();
  }

  [Fact]
  public void GetOrAddWithTheSameIdReturnsTheStoredCapture() {
    var store = new CaptureSessionStore();
    var first = store.GetOrAdd("frame-1", Png());

    var second = store.GetOrAdd("frame-1", Png(8, 8));

    second.Should().BeSameAs(first);
    second.Id.Should().Be("frame-1");
    second.Width.Should().Be(4);
  }

  [Fact]
  public void GetOrAddWithANewIdStoresTheCapture() {
    var store = new CaptureSessionStore();

    var capture = store.GetOrAdd("frame-2", Png());

    capture.Id.Should().Be("frame-2");
    store.TryGet("frame-2", out var found).Should().BeTrue();
    found.Should().BeSameAs(capture);
  }

  [Fact]
  public void TheTrimRuleDoesNotChange() {
    var store = new CaptureSessionStore(maxEntries: 2);

    store.GetOrAdd("a", Png());
    System.Threading.Thread.Sleep(20);
    store.GetOrAdd("b", Png());
    System.Threading.Thread.Sleep(20);
    store.GetOrAdd("c", Png());

    store.TryGet("a", out _).Should().BeFalse("the oldest capture is removed");
    store.TryGet("b", out _).Should().BeTrue();
    store.TryGet("c", out _).Should().BeTrue();
  }
}
