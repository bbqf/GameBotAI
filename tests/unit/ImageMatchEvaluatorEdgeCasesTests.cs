using System;
using System.Drawing;
using GameBot.Domain.Triggers;
using GameBot.Domain.Triggers.Evaluators;
using Xunit;

namespace GameBot.Unit.ImageStorage;

public sealed class ImageMatchEvaluatorEdgeCasesTests
{
  private static Bitmap Solid(Color c, int w = 2, int h = 2)
  {
    var bmp = new Bitmap(w, h);
    using var g = Graphics.FromImage(bmp);
    g.Clear(c);
    return bmp;
  }

  [Fact]
  public void MissingReferenceImageReturnsZeroPending()
  {
    var store = new ReferenceImageStore(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "GameBotEdge", Guid.NewGuid().ToString("N")));
    var src = new SingleBitmapScreenSource(() => Solid(Color.White));
    var eval = new ImageMatchEvaluator(store, src);
    var trig = new Trigger { Id = "e1", Type = TriggerType.ImageMatch, Enabled = true, Params = new ImageMatchParams { ReferenceImageId = "nope", Region = new GameBot.Domain.Triggers.Region{ X=0,Y=0,Width=1,Height=1}, SimilarityThreshold = 0.5 } };
    var res = eval.Evaluate(trig, DateTimeOffset.UtcNow);
    Assert.Equal(TriggerStatus.Pending, res.Status);
    Assert.True(res.Similarity!.Value == 0);
  }

  [Fact]
  public void NullScreenReturnsZeroPending()
  {
    var store = new ReferenceImageStore(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "GameBotEdge", Guid.NewGuid().ToString("N")));
    using var tpl = Solid(Color.White);
    store.AddOrUpdate("tpl", (Bitmap)tpl.Clone());
    var src = new SingleBitmapScreenSource(() => null);
    var eval = new ImageMatchEvaluator(store, src);
    var trig = new Trigger { Id = "e2", Type = TriggerType.ImageMatch, Enabled = true, Params = new ImageMatchParams { ReferenceImageId = "tpl", Region = new GameBot.Domain.Triggers.Region{ X=0,Y=0,Width=1,Height=1}, SimilarityThreshold = 0.5 } };
    var res = eval.Evaluate(trig, DateTimeOffset.UtcNow);
    Assert.Equal(TriggerStatus.Pending, res.Status);
    Assert.True(res.Similarity!.Value == 0);
  }

  [Fact]
  public void TemplateLargerThanRegionReturnsZero()
  {
    var store = new ReferenceImageStore(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "GameBotEdge", Guid.NewGuid().ToString("N")));
    using var tpl = Solid(Color.White, 3, 3);
    store.AddOrUpdate("tpl", (Bitmap)tpl.Clone());
    using var screen = Solid(Color.White, 2, 2);
    var src = new SingleBitmapScreenSource(() => (Bitmap)screen.Clone());
    var eval = new ImageMatchEvaluator(store, src);
    var trig = new Trigger { Id = "e3", Type = TriggerType.ImageMatch, Enabled = true, Params = new ImageMatchParams { ReferenceImageId = "tpl", Region = new GameBot.Domain.Triggers.Region{ X=0,Y=0,Width=1,Height=1}, SimilarityThreshold = 0.5 } };
    var res = eval.Evaluate(trig, DateTimeOffset.UtcNow);
    Assert.Equal(TriggerStatus.Pending, res.Status);
    Assert.True(res.Similarity!.Value == 0);
  }

  [Fact]
  public void ConstantEqualImagesReturnOne()
  {
    var store = new ReferenceImageStore(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "GameBotEdge", Guid.NewGuid().ToString("N")));
    using var tpl = Solid(Color.Gray, 2, 2);
    store.AddOrUpdate("tpl", (Bitmap)tpl.Clone());
    using var screen = Solid(Color.Gray, 2, 2);
    var src = new SingleBitmapScreenSource(() => (Bitmap)screen.Clone());
    var eval = new ImageMatchEvaluator(store, src);
    var trig = new Trigger { Id = "e4", Type = TriggerType.ImageMatch, Enabled = true, Params = new ImageMatchParams { ReferenceImageId = "tpl", Region = new GameBot.Domain.Triggers.Region{ X=0,Y=0,Width=1,Height=1}, SimilarityThreshold = 0.99 } };
    var res = eval.Evaluate(trig, DateTimeOffset.UtcNow);
    Assert.Equal(TriggerStatus.Satisfied, res.Status);
    Assert.True(res.Similarity!.Value >= 0.99);
  }

  [Fact]
  public void ConstantDifferentImagesReturnZero()
  {
    var store = new ReferenceImageStore(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "GameBotEdge", Guid.NewGuid().ToString("N")));
    using var tpl = Solid(Color.White, 2, 2);
    store.AddOrUpdate("tpl", (Bitmap)tpl.Clone());
    using var screen = Solid(Color.Black, 2, 2);
    var src = new SingleBitmapScreenSource(() => (Bitmap)screen.Clone());
    var eval = new ImageMatchEvaluator(store, src);
    var trig = new Trigger { Id = "e5", Type = TriggerType.ImageMatch, Enabled = true, Params = new ImageMatchParams { ReferenceImageId = "tpl", Region = new GameBot.Domain.Triggers.Region{ X=0,Y=0,Width=1,Height=1}, SimilarityThreshold = 0.5 } };
    var res = eval.Evaluate(trig, DateTimeOffset.UtcNow);
    Assert.Equal(TriggerStatus.Pending, res.Status);
    Assert.True(res.Similarity!.Value == 0);
  }
}
