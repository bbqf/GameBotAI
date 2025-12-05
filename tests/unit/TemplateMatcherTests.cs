using System.Threading.Tasks;
using FluentAssertions;
using OpenCvSharp;
using Xunit;
using GameBot.Domain.Vision;

namespace GameBot.UnitTests
{
    public class TemplateMatcherTests
    {
        private static Mat CreateSolid(int width, int height, Scalar color)
        {
            var m = new Mat(new Size(width, height), MatType.CV_8UC3, color);
            return m;
        }

        private static void DrawRect(Mat img, int x, int y, int w, int h, Scalar color)
        {
            Cv2.Rectangle(img, new Rect(x, y, w, h), color, thickness: -1);
        }

        private static void DrawPattern(Mat img, int x, int y)
        {
            // 5x5 black square with gray center pixel to avoid zero-variance template issues
            DrawRect(img, x, y, 5, 5, new Scalar(0, 0, 0));
            img.Set(y + 2, x + 2, new Vec3b(128, 128, 128));
        }

        [Fact(DisplayName = "MatchAllAsync returns empty when no occurrence")]
        public async Task MatchAllAsyncReturnsEmptyWhenNoOccurrence()
        {
            using var screenshot = CreateSolid(50, 50, new Scalar(255, 255, 255));
            using var template = CreateSolid(5, 5, new Scalar(0, 0, 0));
            // make template non-uniform
            template.Set(2, 2, new Vec3b(128, 128, 128));
            var matcher = new TemplateMatcher();
            var cfg = new TemplateMatcherConfig(Threshold: 0.99, MaxResults: 10, Overlap: 0.3);

            var res = await matcher.MatchAllAsync(screenshot, template, cfg);

            res.Matches.Should().BeEmpty();
            res.LimitsHit.Should().BeFalse();
        }

        [Fact(DisplayName = "MatchAllAsync returns two matches for two exact patches")]
        public async Task MatchAllAsyncReturnsTwoMatchesForTwoExactPatches()
        {
            using var screenshot = CreateSolid(60, 40, new Scalar(255, 255, 255));
            // Draw two 5x5 patterns with gray center
            DrawPattern(screenshot, 10, 10);
            DrawPattern(screenshot, 30, 20);

            using var template = CreateSolid(5, 5, new Scalar(0, 0, 0));
            template.Set(2, 2, new Vec3b(128, 128, 128));
            var matcher = new TemplateMatcher();
            var cfg = new TemplateMatcherConfig(Threshold: 0.99, MaxResults: 10, Overlap: 0.3);

            var res = await matcher.MatchAllAsync(screenshot, template, cfg);

            res.Matches.Should().HaveCount(2);
            res.LimitsHit.Should().BeFalse();
            res.Matches[0].BBox.Width.Should().Be(5);
            res.Matches[0].BBox.Height.Should().Be(5);
            res.Matches[1].BBox.Width.Should().Be(5);
            res.Matches[1].BBox.Height.Should().Be(5);
        }
    }
}
