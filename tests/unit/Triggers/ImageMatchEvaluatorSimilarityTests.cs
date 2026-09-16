using System;
using System.Drawing;
using Xunit;
using GameBot.Domain.Triggers;
using GameBot.Domain.Triggers.Evaluators;
using GameBot.Tests.Unit.Triggers;

namespace GameBot.Tests.Unit.Triggers {
  public class ImageMatchEvaluatorSimilarityTests {
    private static Bitmap CreateConstantBitmap(byte value, int w = 8, int h = 8) {
      var bmp = new Bitmap(w, h);
      for (int y = 0; y < h; y++)
        for (int x = 0; x < w; x++)
          bmp.SetPixel(x, y, Color.FromArgb(value, value, value));
      return bmp;
    }

    [Fact]
    public void SimilarityAtThresholdIsSatisfied() {
      var bmp = CreateConstantBitmap(128);
      var store = new StubReferenceImageStore(bmp);
      var screen = new StubScreenSource(bmp);
      var eval = new ImageMatchEvaluator(store, screen);
      var trigger = new Trigger {
        Id = "t1",
        Enabled = true,
        Type = TriggerType.ImageMatch,
        Params = new ImageMatchParams {
          ReferenceImageId = "ref",
          SimilarityThreshold = 0.5,
          Region = new GameBot.Domain.Triggers.Region { X = 0, Y = 0, Width = 1, Height = 1 }
        }
      };
      var result = eval.Evaluate(trigger, DateTimeOffset.UtcNow);
      Assert.Equal(TriggerStatus.Satisfied, result.Status);
      Assert.Equal("similarity_met", result.Reason);
    }

    [Fact]
    public void SimilarityJustBelowThresholdIsPending() {
      var bmp = CreateConstantBitmap(128);
      var bmpScreen = CreateConstantBitmap(100); // visibly different (~11% off)
      var store = new StubReferenceImageStore(bmp);
      var screen = new StubScreenSource(bmpScreen);
      var eval = new ImageMatchEvaluator(store, screen);
      var trigger = new Trigger {
        Id = "t2",
        Enabled = true,
        Type = TriggerType.ImageMatch,
        Params = new ImageMatchParams {
          ReferenceImageId = "ref",
          SimilarityThreshold = 0.99,
          Region = new GameBot.Domain.Triggers.Region { X = 0, Y = 0, Width = 1, Height = 1 }
        }
      };
      var result = eval.Evaluate(trigger, DateTimeOffset.UtcNow);
      Assert.Equal(TriggerStatus.Pending, result.Status);
      Assert.Equal("similarity_below_threshold", result.Reason);
    }

    // ---- Issue #196 (B-013) -------------------------------------------------------------------
    //
    // The trigger path clamps its similarity with Math.Min(1, ...), so before the fix an infinite
    // confidence became a similarity of exactly 1.0 and the trigger fired on a dimmed modal. Its
    // own "constant" check measures the *template's* standard deviation, not the region's, so a
    // detailed template over a flat region went to the matcher path and inherited the defect.

    private static Bitmap CreateMaskedBadgeBitmap(int w = 42, int h = 52) {
      var bmp = new Bitmap(w, h, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
      var cx = (w - 1) / 2.0;
      var cy = (h - 1) / 2.0;
      var radius = Math.Min(cx, cy);
      for (int y = 0; y < h; y++) {
        for (int x = 0; x < w; x++) {
          var dx = (x - cx) / radius;
          var dy = (y - cy) / radius;
          if ((dx * dx) + (dy * dy) <= 1.0) {
            // Hash-derived art so the retained region carries real contrast.
            unchecked {
              var hsh = ((uint)(x + 1) * 73856093u) ^ ((uint)(y + 1) * 19349663u);
              hsh ^= hsh >> 13;
              hsh *= 0x5BD1E995u;
              var v = (int)((hsh ^ (hsh >> 15)) % 256);
              bmp.SetPixel(x, y, Color.FromArgb(255, v, 255 - v, (v * 3) % 256));
            }
          }
          else {
            bmp.SetPixel(x, y, Color.FromArgb(0, 255, 0, 255));
          }
        }
      }
      return bmp;
    }

    private static Trigger MaskedTrigger(string id, double threshold) => new() {
      Id = id,
      Enabled = true,
      Type = TriggerType.ImageMatch,
      Params = new ImageMatchParams {
        ReferenceImageId = "ref",
        SimilarityThreshold = threshold,
        Region = new GameBot.Domain.Triggers.Region { X = 0, Y = 0, Width = 1, Height = 1 }
      }
    };

    [Fact]
    public void MaskedTriggerDoesNotFireOnAFlatRegion() {
      using var template = CreateMaskedBadgeBitmap();
      using var flatScreen = CreateConstantBitmap(200, w: 200, h: 160);
      var eval = new ImageMatchEvaluator(
        new StubReferenceImageStore(template),
        new StubScreenSource(flatScreen),
        new GameBot.Domain.Vision.TemplateMatcher());

      var result = eval.Evaluate(MaskedTrigger("t196-general", 0.85), DateTimeOffset.UtcNow);

      Assert.Equal(TriggerStatus.Pending, result.Status);
      Assert.NotNull(result.Similarity);
      Assert.True(double.IsFinite(result.Similarity!.Value));
      Assert.True(result.Similarity!.Value < 0.85,
        $"a flat region must not satisfy a masked image trigger; got {result.Similarity}");
    }

    [Fact]
    public void MaskedTriggerDoesNotFireOnAFlatRegionOfExactlyTemplateSize() {
      // The same-size shortcut compares mean absolute difference and never divides, so it cannot
      // degenerate the way the correlation did. This pins that.
      using var template = CreateMaskedBadgeBitmap();
      using var flatScreen = CreateConstantBitmap(200, w: 42, h: 52);
      var eval = new ImageMatchEvaluator(
        new StubReferenceImageStore(template),
        new StubScreenSource(flatScreen),
        new GameBot.Domain.Vision.TemplateMatcher());

      var result = eval.Evaluate(MaskedTrigger("t196-samesize", 0.85), DateTimeOffset.UtcNow);

      Assert.Equal(TriggerStatus.Pending, result.Status);
      Assert.NotNull(result.Similarity);
      Assert.True(double.IsFinite(result.Similarity!.Value));
      Assert.True(result.Similarity!.Value < 0.85,
        $"a flat same-size region must not satisfy a masked image trigger; got {result.Similarity}");
    }
  }
}
