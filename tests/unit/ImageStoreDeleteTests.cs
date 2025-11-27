using System;
using System.Drawing;
using System.IO;
using GameBot.Domain.Triggers.Evaluators;
using Xunit;

namespace GameBot.Unit.ImageStorage;

public sealed class ImageStoreDeleteTests {
  private static Bitmap Solid(Color c) {
    var bmp = new Bitmap(1, 1);
    bmp.SetPixel(0, 0, c);
    return bmp;
  }

  [Fact]
  public void OverwriteReplacesContent() {
    var root = Path.Combine(Path.GetTempPath(), "GameBotUnitImages", Guid.NewGuid().ToString("N"));
    var store = new ReferenceImageStore(root);
    using var first = Solid(Color.Red);
    store.AddOrUpdate("sample", (Bitmap)first.Clone());
    using var second = Solid(Color.Blue);
    store.AddOrUpdate("sample", (Bitmap)second.Clone());
    store.TryGet("sample", out var readBmp).ShouldBeTrue();
    var pixel = readBmp.GetPixel(0, 0);
    Assert.Equal(Color.Blue.ToArgb(), pixel.ToArgb());
  }

  [Fact]
  public void DeleteRemovesFile() {
    var root = Path.Combine(Path.GetTempPath(), "GameBotUnitImages", Guid.NewGuid().ToString("N"));
    var store = new ReferenceImageStore(root);
    using var img = Solid(Color.Green);
    store.AddOrUpdate("sample", (Bitmap)img.Clone());
    Assert.True(store.Exists("sample"));
    Assert.True(store.Delete("sample"));
    Assert.False(store.Exists("sample"));
  }
}

internal static class TestExtensions {
  public static void ShouldBeTrue(this bool value) => Assert.True(value);
}