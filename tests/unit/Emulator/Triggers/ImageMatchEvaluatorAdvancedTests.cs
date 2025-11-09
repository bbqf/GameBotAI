using System.Drawing;
using GameBot.Domain.Profiles;
using GameBot.Domain.Profiles.Evaluators;
using Xunit;

namespace GameBot.Unit.Emulator.Triggers;

public sealed class ImageMatchEvaluatorAdvancedTests
{
    private static Bitmap MakeSolid(int w, int h, Color c)
    {
        var bmp = new Bitmap(w, h);
        using var g = Graphics.FromImage(bmp);
        g.Clear(c);
        return bmp;
    }

    [Fact]
    public void ExactMatch_ShouldYieldHighSimilarity()
    {
        var store = new MemoryReferenceImageStore();
        var tpl = MakeSolid(10, 10, Color.White);
        store.Add("tpl", tpl);
        using var screen = MakeSolid(100, 100, Color.White);
        var screenSrc = new SingleBitmapScreenSource(() => (Bitmap)screen.Clone());
        var eval = new ImageMatchEvaluator(store, screenSrc);
        var trig = new ProfileTrigger
        {
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
    public void Mismatch_ShouldYieldLowSimilarity()
    {
        var store = new MemoryReferenceImageStore();
        var tpl = MakeSolid(10, 10, Color.White);
        store.Add("tpl", tpl);
        using var screen = MakeSolid(100, 100, Color.Black);
        var screenSrc = new SingleBitmapScreenSource(() => (Bitmap)screen.Clone());
        var eval = new ImageMatchEvaluator(store, screenSrc);
        var trig = new ProfileTrigger
        {
            Id = "t1",
            Type = TriggerType.ImageMatch,
            Enabled = true,
            Params = new ImageMatchParams { ReferenceImageId = "tpl", Region = new Region { X = 0, Y = 0, Width = 1, Height = 1 }, SimilarityThreshold = 0.9 }
        };
        var res = eval.Evaluate(trig, DateTimeOffset.UtcNow);
        Assert.Equal(TriggerStatus.Pending, res.Status);
        Assert.True(res.Similarity!.Value <= 0.1);
    }

    [Fact]
    public void RegionSmallerThanTemplate_ShouldNotMatch()
    {
        var store = new MemoryReferenceImageStore();
        var tpl = MakeSolid(50, 50, Color.White);
        store.Add("tpl", tpl);
        using var screen = MakeSolid(100, 100, Color.White);
        // Region 10x10 smaller than 50x50 template
        var screenSrc = new SingleBitmapScreenSource(() => (Bitmap)screen.Clone());
        var eval = new ImageMatchEvaluator(store, screenSrc);
        var trig = new ProfileTrigger
        {
            Id = "t1",
            Type = TriggerType.ImageMatch,
            Enabled = true,
            Params = new ImageMatchParams { ReferenceImageId = "tpl", Region = new Region { X = 0, Y = 0, Width = 0.1, Height = 0.1 }, SimilarityThreshold = 0.9 }
        };
        var res = eval.Evaluate(trig, DateTimeOffset.UtcNow);
        Assert.Equal(TriggerStatus.Pending, res.Status);
        Assert.True(res.Similarity!.Value <= 0.01);
    }
}
