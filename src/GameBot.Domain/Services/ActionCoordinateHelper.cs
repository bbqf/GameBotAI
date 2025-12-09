using System;
using System.Runtime.Versioning;
using GameBot.Domain.Commands;
using GameBot.Domain.Vision;
using OpenCvSharp;

namespace GameBot.Domain.Services
{
    [SupportedOSPlatform("windows")]
    public sealed class ActionCoordinateHelper
    {
        private readonly CommandRunner _runner;

        public ActionCoordinateHelper(ITemplateMatcher matcher)
        {
            _runner = new CommandRunner(matcher);
        }

        /// <summary>
        /// Resolves coordinates for a tap action using a DetectionTarget and provided Mats.
        /// Returns null and sets error when zero/multiple detections or invalid inputs.
        /// </summary>
        public ResolvedCoordinate? ResolveTapCoordinates(DetectionTarget target, Mat screenMat, Mat templateMat, double threshold, out string? error)
        {
            // For center-only resolution, limit to maxResults=1 to enforce uniqueness in simple scenarios
            return _runner.ResolveCoordinates(target, screenMat, templateMat, threshold, out error, maxResults: 1);
        }
    }
}
