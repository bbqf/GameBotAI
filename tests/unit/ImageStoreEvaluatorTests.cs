using System;
using System.Drawing;
using System.IO;
using GameBot.Domain.Triggers;
using GameBot.Domain.Triggers.Evaluators;
using Xunit;

namespace GameBot.Unit.ImageStorage;

public sealed class ImageStoreEvaluatorTests {
  private static Bitmap Solid(Color c) {
    var bmp = new Bitmap(5, 5);
    using var g = Graphics.FromImage(bmp);
    g.Clear(c);
    return bmp;
  }

  [Fact]
  public void DiskBackedStoreAllowsEvaluatorToResolveAndMatch() {
    var root = Path.Combine(Path.GetTempPath(), "GameBotUnitImages", Guid.NewGuid().ToString("N"));
    var store = new ReferenceImageStore(root);
    using var tpl = Solid(Color.White);
    store.AddOrUpdate("tpl", (Bitmap)tpl.Clone());
    using var screen = Solid(Color.White);
    var src = new SingleBitmapScreenSource(() => (Bitmap)screen.Clone());
    var eval = new ImageMatchEvaluator(store, src);
    var trig = new Trigger {
      Id = "t1",
      Type = TriggerType.ImageMatch,
      Enabled = true,
      Params = new ImageMatchParams { ReferenceImageId = "tpl", Region = new Region { X = 0, Y = 0, Width = 1, Height = 1 }, SimilarityThreshold = 0.9 }
    };
    var res = eval.Evaluate(trig, DateTimeOffset.UtcNow);
    Assert.Equal(TriggerStatus.Satisfied, res.Status);
    Assert.True(res.Similarity!.Value >= 0.95);
  }

  [Fact]
  public void DiskBackedStoreNonMatchingScreenPending() {
    var root = Path.Combine(Path.GetTempPath(), "GameBotUnitImages", Guid.NewGuid().ToString("N"));
    var store = new ReferenceImageStore(root);
    using var tpl = Solid(Color.White);
    store.AddOrUpdate("tpl", (Bitmap)tpl.Clone());
    using var screen = Solid(Color.Black);
    var src = new SingleBitmapScreenSource(() => (Bitmap)screen.Clone());
    var eval = new ImageMatchEvaluator(store, src);
    var trig = new Trigger {
      Id = "t2",
      Type = TriggerType.ImageMatch,
      Enabled = true,
      Params = new ImageMatchParams { ReferenceImageId = "tpl", Region = new Region { X = 0, Y = 0, Width = 1, Height = 1 }, SimilarityThreshold = 0.9 }
    };
    var res = eval.Evaluate(trig, DateTimeOffset.UtcNow);
    Assert.Equal(TriggerStatus.Pending, res.Status);
    Assert.True(res.Similarity!.Value <= 0.1);
  }
}