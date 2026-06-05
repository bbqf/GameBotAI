using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.Versioning;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using GameBot.Domain.Images;
using GameBot.Service.Services;
using Xunit;

namespace GameBot.Tests.Unit;

[SupportedOSPlatform("windows")]
public sealed class ImageDetectAllEndpointTests {
  private static byte[] CreateMinimalPng(int w = 8, int h = 8) {
    using var bmp = new Bitmap(w, h, PixelFormat.Format24bppRgb);
    using var ms = new MemoryStream();
    bmp.Save(ms, ImageFormat.Png);
    return ms.ToArray();
  }

  [Fact(DisplayName = "CaptureSessionStore returns false for unknown captureId")]
  public void CaptureSessionStoreReturnsFalseForUnknownId() {
    var store = new CaptureSessionStore();
    store.TryGet("not-a-real-id", out var capture).Should().BeFalse();
    capture.Should().BeNull();
  }

  [Fact(DisplayName = "CaptureSessionStore stores and retrieves a PNG by id")]
  public void CaptureSessionStoreStoresAndRetrievesByid() {
    var store = new CaptureSessionStore();
    var png = CreateMinimalPng();
    var session = store.Add(png);
    session.Id.Should().NotBeNullOrEmpty();
    session.Png.Should().Equal(png);
    store.TryGet(session.Id, out var retrieved).Should().BeTrue();
    retrieved.Should().NotBeNull();
    retrieved!.Id.Should().Be(session.Id);
  }

  [Fact(DisplayName = "IImageRepository.ListIdsAsync returns empty when no images stored")]
  public async Task ImageRepositoryListIdsReturnsEmptyWhenNoImages() {
    var tempDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
    try {
      Directory.CreateDirectory(tempDir);
      var repo = new FileImageRepository(tempDir);
      var ids = await repo.ListIdsAsync(CancellationToken.None);
      ids.Should().BeEmpty();
    }
    finally {
      Directory.Delete(tempDir, recursive: true);
    }
  }

  [Fact(DisplayName = "IImageRepository.ListIdsAsync returns id for a saved PNG")]
  public async Task ImageRepositoryListIdsReturnsIdForSavedPng() {
    var tempDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
    try {
      Directory.CreateDirectory(tempDir);
      var repo = new FileImageRepository(tempDir);
      var png = CreateMinimalPng();
      await repo.SaveAsync("test-image", new MemoryStream(png), "image/png", "test-image.png", overwrite: false, CancellationToken.None);

      var ids = await repo.ListIdsAsync(CancellationToken.None);
      ids.Should().Contain("test-image");
    }
    finally {
      Directory.Delete(tempDir, recursive: true);
    }
  }
}
