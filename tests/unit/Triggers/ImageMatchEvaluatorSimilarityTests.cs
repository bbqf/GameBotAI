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
            var bmpScreen = CreateConstantBitmap(127); // slightly different
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
    }
}
