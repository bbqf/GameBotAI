using System;
using System.Collections.Generic;
using System.Runtime.Versioning;
using GameBot.Domain.Vision;
using OpenCvSharp;

namespace GameBot.Domain.Commands.Execution {
  [SupportedOSPlatform("windows")]
  public sealed class DetectionCoordinateResolver {
    private readonly ITemplateMatcher _matcher;

    public DetectionCoordinateResolver(ITemplateMatcher matcher) {
      _matcher = matcher;
    }

    public ResolvedCoordinate? ResolveCenter(DetectionTarget target, Mat screenMat, Mat templateMat, TemplateMatcherConfig config, out string? error) {
      error = null;
      ArgumentNullException.ThrowIfNull(target);
      if (screenMat == null || templateMat == null || config == null) {
        error = "screen/template/config is null";
        return null;
      }

      var passing = FindPassingMatches(target, screenMat, templateMat, config);

      if (passing.Count == 0) {
        error = "no detection above threshold";
        return null;
      }

      if (passing.Count > 1) {
        error = $"multiple detections ({passing.Count}) above threshold";
        return null;
      }

      var (box, score) = passing[0];
      var centerX = box.X + box.Width / 2;
      var centerY = box.Y + box.Height / 2;

      var x = centerX + target.OffsetX;
      var y = centerY + target.OffsetY;

      // clamp
      var screenWidth = screenMat.Cols;
      var screenHeight = screenMat.Rows;
      if (x < 0) x = 0; else if (x >= screenWidth) x = screenWidth - 1;
      if (y < 0) y = 0; else if (y >= screenHeight) y = screenHeight - 1;

      return new ResolvedCoordinate(x, y, score, target.ReferenceImageId, box);
    }
    public ResolvedCoordinate? ResolveCenter(DetectionTarget target, Mat screenMat, Mat templateMat, TemplateMatcherConfig config, out string? error, DetectionSelectionStrategy strategy = DetectionSelectionStrategy.HighestConfidence) {
      // Delegate to main method then apply selection by filtering passing list based on strategy.
      error = null;
      ArgumentNullException.ThrowIfNull(target);
      if (screenMat == null || templateMat == null || config == null) {
        error = "screen/template/config is null";
        return null;
      }
      var passing = FindPassingMatches(target, screenMat, templateMat, config);
      if (passing.Count == 0) { error = "no detection above threshold"; return null; }
      var (box, score) = strategy == DetectionSelectionStrategy.HighestConfidence
          ? passing.OrderByDescending(p => p.score).First()
          : passing.First();
      var centerX = box.X + box.Width / 2;
      var centerY = box.Y + box.Height / 2;
      var x = centerX + target.OffsetX;
      var y = centerY + target.OffsetY;
      var screenWidth = screenMat.Cols;
      var screenHeight = screenMat.Rows;
      if (x < 0) x = 0; else if (x >= screenWidth) x = screenWidth - 1;
      if (y < 0) y = 0; else if (y >= screenHeight) y = screenHeight - 1;
      return new ResolvedCoordinate(x, y, score, target.ReferenceImageId, box);
    }

    /// <summary>
    /// Matches the template and keeps the matches at or above the target confidence. When the target has a
    /// region (feature 130), the screen is cropped to the part of the region inside the capture first, so a
    /// stronger match outside the region cannot take a result slot. Each box is moved back to full-capture
    /// pixels. An empty clip, or a clip smaller than the template, gives no match and no error.
    /// </summary>
    private List<(BoundingBox box, double score)> FindPassingMatches(DetectionTarget target, Mat screenMat, Mat templateMat, TemplateMatcherConfig config) {
      var passing = new List<(BoundingBox box, double score)>();
      var originX = 0;
      var originY = 0;
      Mat? crop = null;
      try {
        var searchMat = screenMat;
        if (target.Region is not null) {
          var clipped = target.Region.ClipTo(screenMat.Cols, screenMat.Rows);
          if (clipped is null || templateMat.Cols > clipped.Width || templateMat.Rows > clipped.Height) {
            return passing;
          }
          crop = screenMat.SubMat(clipped.Y, clipped.Y + clipped.Height, clipped.X, clipped.X + clipped.Width);
          searchMat = crop;
          originX = clipped.X;
          originY = clipped.Y;
        }

        var result = _matcher.MatchAllAsync(searchMat, templateMat, config).GetAwaiter().GetResult();
        foreach (var d in result.Matches) {
          if (d.Confidence >= target.Confidence) {
            var b = d.BBox;
            passing.Add((new BoundingBox(b.X + originX, b.Y + originY, b.Width, b.Height), d.Confidence));
          }
        }
        return passing;
      }
      finally {
        crop?.Dispose();
      }
    }
  }
}
