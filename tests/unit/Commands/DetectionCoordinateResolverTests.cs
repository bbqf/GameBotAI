using System;
using FluentAssertions;
using GameBot.Domain.Commands;
using GameBot.Domain.Commands.Execution;
using GameBot.Domain.Vision;
using OpenCvSharp;
using Xunit;

namespace GameBot.UnitTests.Commands
{
    public class DetectionCoordinateResolverTests
    {
        [Fact]
        public void ResolveCenterReturnsSingleCenterWhenExactlyOneDetection()
        {
            // Arrange: create a simple screenshot with a distinct template square
            using var screen = new Mat(20, 20, MatType.CV_8UC1, Scalar.All(0));
            using var template = new Mat(4, 4, MatType.CV_8UC1, Scalar.All(255));
            // place template region at (0,0)
            var roi = new Rect(0, 0, template.Cols, template.Rows);
            using (var sub = new Mat(screen, roi))
            {
                template.CopyTo(sub);
            }

            var matcher = new TemplateMatcher();
            var resolver = new DetectionCoordinateResolver(matcher);
            var target = new DetectionTarget("tpl", 0.99);
            var cfg = new TemplateMatcherConfig(0.99, 1, 0.3);

            // Act
            var coord = resolver.ResolveCenter(target, screen, template, cfg, out var error);

            // Assert
            error.Should().BeNull();
            coord.Should().NotBeNull();
            coord!.Confidence.Should().BeGreaterOrEqualTo(0.8);
            coord.BBox.X.Should().Be(0);
            coord.BBox.Y.Should().Be(0);
            coord.X.Should().Be(template.Cols / 2);
            coord.Y.Should().Be(template.Rows / 2);
        }

        [Fact]
        public void ResolveCenterReturnsNullWhenMultipleDetections()
        {
            // Arrange: two identical template regions
            using var screen = new Mat(20, 20, MatType.CV_8UC1, Scalar.All(0));
            using var template = new Mat(4, 4, MatType.CV_8UC1, Scalar.All(255));
            var r1 = new Rect(2, 2, template.Cols, template.Rows);
            var r2 = new Rect(12, 12, template.Cols, template.Rows);
            using (var sub = new Mat(screen, r1)) { template.CopyTo(sub); }
            using (var sub = new Mat(screen, r2)) { template.CopyTo(sub); }

            var matcher = new TemplateMatcher();
            var resolver = new DetectionCoordinateResolver(matcher);
            var target = new DetectionTarget("tpl", 0.99);
            var cfg = new TemplateMatcherConfig(0.99, 10, 0.3);

            // Act
            var coord = resolver.ResolveCenter(target, screen, template, cfg, out var error);

            // Assert
            coord.Should().BeNull();
            error.Should().NotBeNull();
            error!.Should().Contain("multiple detections");
        }

        [Fact]
        public void ResolveCenterClampsOutOfBoundsWithOffsets()
        {
            using var screen = new Mat(10, 10, MatType.CV_8UC1, Scalar.All(0));
            using var template = new Mat(4, 4, MatType.CV_8UC1, Scalar.All(255));
            var roi = new Rect(6, 6, template.Cols, template.Rows); // near bottom-right, still in-bounds
            using (var sub = new Mat(screen, roi)) { template.CopyTo(sub); }

            var matcher = new TemplateMatcher();
            var resolver = new DetectionCoordinateResolver(matcher);
            var target = new DetectionTarget("tpl", 0.5, offsetX: 100, offsetY: 100);
            var cfg = new TemplateMatcherConfig(0.5, 1, 0.3);

            var coord = resolver.ResolveCenter(target, screen, template, cfg, out var error);

            error.Should().BeNull();
            coord.Should().NotBeNull();
            coord!.X.Should().Be(9);
            coord.Y.Should().Be(9);
        }

        [Fact]
        public void ResolveCenterSelectsHighestConfidenceWhenMultiple()
        {
            // synthetic: two matches via fake matcher
            var matches = new[]
            {
                (new BoundingBox(0,0,2,2), 0.7),
                (new BoundingBox(5,5,2,2), 0.95)
            };
            var matcher = new FakeMatcher(matches);
            using var screen = new Mat(10, 10, MatType.CV_8UC1, Scalar.All(0));
            using var template = new Mat(2, 2, MatType.CV_8UC1, Scalar.All(255));
            var resolver = new DetectionCoordinateResolver(matcher);
            var target = new DetectionTarget("ref", 0.5);
            var cfg = new TemplateMatcherConfig(0.5, 10, 0.3);
            var coord = resolver.ResolveCenter(target, screen, template, cfg, out var error, DetectionSelectionStrategy.HighestConfidence);
            error.Should().BeNull();
            coord.Should().NotBeNull();
            coord!.Confidence.Should().BeApproximately(0.95f, 0.0001f);
        }

        [Fact]
        public void ResolveCenterSelectsFirstMatchWhenStrategyIsFirstMatch()
        {
            var matches = new[]
            {
                (new BoundingBox(3,3,2,2), 0.6),
                (new BoundingBox(7,7,2,2), 0.95)
            };
            var matcher = new FakeMatcher(matches);
            using var screen = new Mat(12, 12, MatType.CV_8UC1, Scalar.All(0));
            using var template = new Mat(2, 2, MatType.CV_8UC1, Scalar.All(255));
            var resolver = new DetectionCoordinateResolver(matcher);
            var target = new DetectionTarget("ref", 0.5);
            var cfg = new TemplateMatcherConfig(0.5, 10, 0.3);
            var coord = resolver.ResolveCenter(target, screen, template, cfg, out var error, DetectionSelectionStrategy.FirstMatch);
            error.Should().BeNull();
            coord.Should().NotBeNull();
            coord!.BBox.X.Should().Be(3);
            coord!.BBox.Y.Should().Be(3);
            coord!.Confidence.Should().BeApproximately(0.6f, 0.0001f);
        }

        private sealed class FakeMatcher : ITemplateMatcher
        {
            private readonly (BoundingBox box, double conf)[] _matches;
            public FakeMatcher((BoundingBox box, double conf)[] matches) { _matches = matches; }
            public Task<TemplateMatchResult> MatchAllAsync(Mat screenshot, Mat templateMat, TemplateMatcherConfig config, System.Threading.CancellationToken cancellationToken = default)
                => Task.FromResult(new TemplateMatchResult(_matches.Select(m => new TemplateMatch(m.box, m.conf)).ToList(), false));
        }
    }
}
