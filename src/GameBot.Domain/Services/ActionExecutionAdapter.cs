using System;
using System.Runtime.Versioning;
using GameBot.Domain.Actions;
using GameBot.Domain.Commands;
using GameBot.Domain.Vision;
using OpenCvSharp;

namespace GameBot.Domain.Services
{
    [SupportedOSPlatform("windows")]
    public sealed class ActionExecutionAdapter
    {
        private readonly CommandRunner _runner;

        public ActionExecutionAdapter(ITemplateMatcher matcher)
        {
            _runner = new CommandRunner(matcher);
        }

        /// <summary>
        /// Applies detection-based coordinates to a tap action when a DetectionTarget is provided.
        /// Returns false and sets error when coordinates cannot be resolved (zero/multiple).
        /// </summary>
        public bool TryApplyDetectionCoordinates(InputAction action, DetectionTarget? target, Mat screenMat, Mat templateMat, double threshold, out string? error, DetectionSelectionStrategy strategy = DetectionSelectionStrategy.HighestConfidence)
        {
            error = null;
            if (action == null) { error = "action is null"; return false; }
            if (!string.Equals(action.Type, "tap", StringComparison.OrdinalIgnoreCase)) { return true; }
            if (target is null) { return true; }

            var maxResults = threshold >= 0.99 ? 1 : 10;
            var effective = target.SelectionStrategy;
            var coord = _runner.ResolveCoordinates(target!, screenMat, templateMat, threshold, out error, maxResults, effective);
            if (coord is null) return false;

            action.Args["x"] = coord.X;
            action.Args["y"] = coord.Y;
            action.Args["confidence"] = coord.Confidence;
            return true;
        }
    }
}
