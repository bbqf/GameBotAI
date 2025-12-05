using FluentAssertions;
using GameBot.Domain.Actions;
using GameBot.Domain.Commands;
using GameBot.Domain.Services;
using GameBot.Domain.Vision;
using OpenCvSharp;
using Xunit;

namespace GameBot.UnitTests.Commands
{
    public class ActionExecutionAdapterTests
    {
        [Fact]
        public void TryApplyDetectionCoordinatesSetsTapXY()
        {
            using var screen = new Mat(16, 16, MatType.CV_8UC1, Scalar.All(0));
            using var template = new Mat(4, 4, MatType.CV_8UC1, Scalar.All(255));
            var roi = new Rect(0, 0, template.Cols, template.Rows);
            using (var sub = new Mat(screen, roi)) { template.CopyTo(sub); }

            var matcher = new TemplateMatcher();
            var adapter = new ActionExecutionAdapter(matcher);
            var target = new DetectionTarget("tpl", 1.0);
            var action = new InputAction { Type = "tap" };

            var ok = adapter.TryApplyDetectionCoordinates(action, target, screen, template, 1.0, out var error);

            ok.Should().BeTrue();
            error.Should().BeNull();
            action.Args.Should().ContainKey("x");
            action.Args.Should().ContainKey("y");
            ((int)action.Args["x"]).Should().Be(template.Cols / 2);
            ((int)action.Args["y"]).Should().Be(template.Rows / 2);
        }

        [Fact]
        public void TryApplyDetectionCoordinatesSelectsHighestConfidenceWhenMultiple()
        {
            using var screen = new Mat(20, 20, MatType.CV_8UC1, Scalar.All(0));
            using var template = new Mat(4, 4, MatType.CV_8UC1, Scalar.All(255));
            var r1 = new Rect(0, 0, template.Cols, template.Rows);
            var r2 = new Rect(10, 10, template.Cols, template.Rows);
            using (var sub = new Mat(screen, r1)) { template.CopyTo(sub); }
            using (var sub = new Mat(screen, r2)) { template.CopyTo(sub); }

            var matcher = new TemplateMatcher();
            var adapter = new ActionExecutionAdapter(matcher);
            var target = new DetectionTarget("tpl", 0.9);
            var action = new InputAction { Type = "tap" };

            var ok = adapter.TryApplyDetectionCoordinates(action, target, screen, template, 0.9, out var error);

            ok.Should().BeTrue();
            error.Should().BeNull();
            action.Args.Should().ContainKey("x");
            action.Args.Should().ContainKey("y");
            var x = (int)action.Args["x"];
            var y = (int)action.Args["y"];
            var cx1 = r1.X + template.Cols / 2;
            var cy1 = r1.Y + template.Rows / 2;
            var cx2 = r2.X + template.Cols / 2;
            var cy2 = r2.Y + template.Rows / 2;
            (x == cx1 || x == cx2).Should().BeTrue();
            (y == cy1 || y == cy2).Should().BeTrue();
        }

        [Fact]
        public void TryApplyDetectionCoordinatesHonorsFirstMatchStrategy()
        {
            using var screen = new Mat(20, 20, MatType.CV_8UC1, Scalar.All(0));
            using var template = new Mat(4, 4, MatType.CV_8UC1, Scalar.All(255));
            var r1 = new Rect(0, 0, template.Cols, template.Rows);
            var r2 = new Rect(10, 10, template.Cols, template.Rows);
            using (var sub = new Mat(screen, r1)) { template.CopyTo(sub); }
            using (var sub = new Mat(screen, r2)) { template.CopyTo(sub); }

            var matcher = new TemplateMatcher();
            var adapter = new ActionExecutionAdapter(matcher);
            var target = new DetectionTarget("tpl", 0.5, selectionStrategy: DetectionSelectionStrategy.FirstMatch);
            var action = new InputAction { Type = "tap" };

            var ok = adapter.TryApplyDetectionCoordinates(action, target, screen, template, 0.5, out var error);

            ok.Should().BeTrue();
            error.Should().BeNull();
            var x = (int)action.Args["x"];
            var y = (int)action.Args["y"];
            var cx1 = r1.X + template.Cols / 2;
            var cy1 = r1.Y + template.Rows / 2;
            x.Should().Be(cx1);
            y.Should().Be(cy1);
        }
    }
}
