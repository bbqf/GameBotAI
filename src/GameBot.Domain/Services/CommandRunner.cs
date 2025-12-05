using System;
using System.Runtime.Versioning;
using GameBot.Domain.Commands;
using GameBot.Domain.Commands.Execution;
using GameBot.Domain.Vision;
using OpenCvSharp;

namespace GameBot.Domain.Services
{
    [SupportedOSPlatform("windows")]
    public sealed class CommandRunner
    {
        private readonly DetectionCoordinateResolver _resolver;

        public CommandRunner(ITemplateMatcher matcher)
        {
            _resolver = new DetectionCoordinateResolver(matcher);
        }

        public ResolvedCoordinate? ResolveCoordinates(DetectionTarget target, Mat screenMat, Mat templateMat, double threshold, out string? error, int maxResults = 10, DetectionSelectionStrategy strategy = DetectionSelectionStrategy.HighestConfidence)
        {
            var config = new TemplateMatcherConfig(threshold, maxResults, 0.3);
            return _resolver.ResolveCenter(target, screenMat, templateMat, config, out error, strategy);
        }
    }
}
