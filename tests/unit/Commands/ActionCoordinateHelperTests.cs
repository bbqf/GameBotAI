using FluentAssertions;
using GameBot.Domain.Commands;
using GameBot.Domain.Services;
using GameBot.Domain.Vision;
using OpenCvSharp;
using Xunit;

namespace GameBot.UnitTests.Commands
{
    public class ActionCoordinateHelperTests
    {
        [Fact]
        public void ResolveTapCoordinatesReturnsCenter()
        {
            using var screen = new Mat(12, 12, MatType.CV_8UC1, Scalar.All(0));
            using var template = new Mat(4, 4, MatType.CV_8UC1, Scalar.All(255));
            var roi = new Rect(0, 0, template.Cols, template.Rows);
            using (var sub = new Mat(screen, roi)) { template.CopyTo(sub); }

            var matcher = new TemplateMatcher();
            var helper = new ActionCoordinateHelper(matcher);
            var target = new DetectionTarget("tpl", 1.0);

            var coord = helper.ResolveTapCoordinates(target, screen, template, 1.0, out var error);

            error.Should().BeNull();
            coord.Should().NotBeNull();
            coord!.X.Should().Be(template.Cols / 2);
            coord.Y.Should().Be(template.Rows / 2);
        }
    }
}
