using System;
using System.Collections.Generic;
using System.Linq;
using GameBot.Domain.Triggers.Evaluators;

namespace GameBot.Domain.Images {
  /// <summary>Outcome of validating an alternates list. <see cref="Ids"/> names the offending entries.</summary>
  public sealed record ImageAlternatesValidationResult(bool IsValid, string? Message, string? Hint, IReadOnlyList<string> Ids) {
    public static ImageAlternatesValidationResult Valid { get; } = new(true, null, null, Array.Empty<string>());
  }

  /// <summary>Rules for an image's alternates list (feature 097).</summary>
  public static class ImageAlternatesValidator {
    /// <summary>The most alternates one image may carry; bounds detection cost at nine matches.</summary>
    public const int MaxAlternates = 8;

    /// <summary>
    /// Validates <paramref name="alternates"/> for <paramref name="primaryId"/>: at most
    /// <see cref="MaxAlternates"/> entries, each a valid id of a stored image, none equal to the primary,
    /// no duplicates (case-insensitive).
    /// </summary>
    /// <param name="exists">Whether an image id is stored.</param>
    public static ImageAlternatesValidationResult Validate(string primaryId, IReadOnlyList<string> alternates, Func<string, bool> exists) {
      ArgumentNullException.ThrowIfNull(alternates);
      ArgumentNullException.ThrowIfNull(exists);

      if (alternates.Count > MaxAlternates) {
        return new(false, $"An image may have at most {MaxAlternates} alternates; {alternates.Count} were given.",
          "Keep the crops that cover distinct renderings and remove the rest.", Array.Empty<string>());
      }

      var invalid = alternates.Where(a => !ReferenceImageIdValidator.IsValid(a)).Select(a => a ?? "").ToArray();
      if (invalid.Length > 0) {
        return new(false, "Alternate ids must be alphanumeric/dash/underscore (1-128 chars).",
          "Use the ids of images uploaded through POST /api/images.", invalid);
      }

      var self = alternates.Where(a => string.Equals(a, primaryId, StringComparison.OrdinalIgnoreCase)).ToArray();
      if (self.Length > 0) {
        return new(false, "An image cannot be its own alternate.", "Remove the image's own id from the list.", self);
      }

      var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
      var duplicates = alternates.Where(a => !seen.Add(a)).ToArray();
      if (duplicates.Length > 0) {
        return new(false, "Alternates must not repeat an id.", "List each alternate once.", duplicates);
      }

      var unknown = alternates.Where(a => !exists(a)).ToArray();
      if (unknown.Length > 0) {
        return new(false, "Every alternate must be a stored image.",
          "Upload the crop through POST /api/images first, then set it as an alternate.", unknown);
      }

      return ImageAlternatesValidationResult.Valid;
    }
  }
}
