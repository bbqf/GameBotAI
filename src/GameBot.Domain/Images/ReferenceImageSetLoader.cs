using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.Versioning;
using GameBot.Domain.Triggers.Evaluators;
using GameBot.Domain.Vision;
using Microsoft.Extensions.Logging;
using OpenCvSharp;

namespace GameBot.Domain.Images {
  /// <summary>
  /// A named reference image with the alternates that also count as a match for it (feature 097).
  /// </summary>
  /// <param name="PrimaryId">The id detections name.</param>
  /// <param name="Primary">The named image.</param>
  /// <param name="Alternates">Stored direct alternates, in registered order.</param>
  /// <param name="MissingAlternateIds">Listed alternates whose image is no longer stored; skipped.</param>
  [SupportedOSPlatform("windows")]
  public sealed record ReferenceImageSet(
      string PrimaryId,
      Bitmap Primary,
      IReadOnlyList<(string Id, Bitmap Image)> Alternates,
      IReadOnlyList<string> MissingAlternateIds) {

    /// <summary>
    /// Returns the matcher a detection of this set should use. Without alternates that is
    /// <paramref name="inner"/> itself — never a wrapper — so single-image scoring stays untouched.
    /// </summary>
    /// <param name="inner">The single-template matcher.</param>
    /// <param name="toMat">Converts an alternate exactly as the caller converts the primary.</param>
    /// <param name="logger">Receives the matched-alternate log line.</param>
    /// <returns>A lease owning any converted alternate Mats; dispose it after matching.</returns>
    public ReferenceSetMatcherLease CreateMatcher(ITemplateMatcher inner, Func<Bitmap, Mat> toMat, ILogger? logger) {
      ArgumentNullException.ThrowIfNull(inner);
      ArgumentNullException.ThrowIfNull(toMat);
      if (Alternates.Count == 0) return new ReferenceSetMatcherLease(inner, Array.Empty<Mat>());

      var mats = new List<(string Id, Mat Template)>(Alternates.Count);
      try {
        foreach (var (id, image) in Alternates) mats.Add((id, toMat(image)));
      }
      catch {
        foreach (var (_, mat) in mats) mat.Dispose();
        throw;
      }
      return new ReferenceSetMatcherLease(new ReferenceSetTemplateMatcher(inner, PrimaryId, mats, logger), mats.ConvertAll(m => m.Template));
    }

    /// <summary>Logs one warning naming every listed alternate that is no longer stored; nothing otherwise.</summary>
    public void LogMissingAlternates(ILogger? logger) {
      if (logger is null || MissingAlternateIds.Count == 0) return;
      ImageAlternatesLog.MissingAlternates(logger, PrimaryId, string.Join(", ", MissingAlternateIds));
    }
  }

  internal static partial class ImageAlternatesLog {
    [LoggerMessage(Level = Microsoft.Extensions.Logging.LogLevel.Warning, Message = "Reference image '{PrimaryId}' lists alternates that are not stored and were skipped: {MissingIds}")]
    public static partial void MissingAlternates(ILogger logger, string primaryId, string missingIds);
  }

  /// <summary>The matcher for one detection plus the alternate templates it borrowed.</summary>
  public sealed class ReferenceSetMatcherLease : IDisposable {
    private readonly IReadOnlyList<Mat> _owned;

    internal ReferenceSetMatcherLease(ITemplateMatcher matcher, IReadOnlyList<Mat> owned) {
      Matcher = matcher;
      _owned = owned;
    }

    /// <summary>The matcher to hand to the detection code.</summary>
    public ITemplateMatcher Matcher { get; }

    public void Dispose() {
      foreach (var mat in _owned) mat.Dispose();
    }
  }

  /// <summary>Loads a <see cref="ReferenceImageSet"/> (feature 097).</summary>
  [SupportedOSPlatform("windows")]
  public static class ReferenceImageSetLoader {
    /// <summary>
    /// Loads <paramref name="id"/> and its stored direct alternates. Alternates are never expanded
    /// transitively.
    /// </summary>
    /// <param name="store">Image store.</param>
    /// <param name="alternates">Alternates repository; null means no image has alternates.</param>
    /// <param name="id">The named image.</param>
    /// <param name="set">The loaded set, or null when the named image is not stored.</param>
    /// <returns>False when the named image is not stored.</returns>
    public static bool TryLoad(IReferenceImageStore store, IImageAlternatesRepository? alternates, string id, out ReferenceImageSet? set) {
      ArgumentNullException.ThrowIfNull(store);
      set = null;
      if (string.IsNullOrWhiteSpace(id) || !store.TryGet(id, out var primary) || primary is null) return false;

      var loaded = new List<(string Id, Bitmap Image)>();
      var missing = new List<string>();
      foreach (var altId in alternates?.GetAlternates(id) ?? Array.Empty<string>()) {
        if (store.TryGet(altId, out var bmp) && bmp is not null) loaded.Add((altId, bmp));
        else missing.Add(altId);
      }

      set = new ReferenceImageSet(id, primary, loaded, missing);
      return true;
    }
  }
}
