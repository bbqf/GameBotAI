using System.Collections.ObjectModel;
using GameBot.Domain.Commands;
using GameBot.Service.Models;

namespace GameBot.Service.Endpoints;

/// <summary>
/// Feature 180: the rules of the optional <c>referenceImageIds</c> list of a detection target. Only a
/// WaitForImage step accepts the list. The commands endpoint and the steps endpoint share this class.
/// </summary>
internal static class DetectionTargetListRules {
  /// <summary>True when the target has a written list.</summary>
  public static bool HasList(DetectionTargetDto? target) => target?.ReferenceImageIds is not null;

  /// <summary>
  /// Checks the list of a WaitForImage target. Returns an error text for HTTP 400, or null when valid.
  /// </summary>
  /// <param name="target">The target, or null.</param>
  /// <param name="path">The field path of the target, for example <c>waitForImage.detectionTarget</c>.</param>
  public static string? ValidateWaitTarget(DetectionTargetDto? target, string path) {
    if (target?.ReferenceImageIds is null) return null;
    ImageListRules.TryValidateWritten(
      target.ReferenceImageId, target.ReferenceImageIds.ToList(), null, $"{path}.referenceImageIds", out var errors);
    return errors.Count == 0 ? null : string.Join("; ", errors);
  }

  /// <summary>Refuses the list on a step that takes one image. Returns an error text, or null when no list is set.</summary>
  /// <param name="target">The target, or null.</param>
  /// <param name="path">The field path of the target.</param>
  public static string? RejectList(DetectionTargetDto? target, string path) =>
    HasList(target) ? $"{path}.referenceImageIds is allowed only for WaitForImage steps" : null;

  /// <summary>True when the target names an image: one id, or a written list.</summary>
  /// <param name="target">The target.</param>
  public static bool HasImage(DetectionTargetDto target) =>
    !string.IsNullOrWhiteSpace(target.ReferenceImageId) || (target.ReferenceImageIds?.Any(i => !string.IsNullOrWhiteSpace(i)) ?? false);

  /// <summary>
  /// Builds the domain target of a WaitForImage step. A written list is normalized, and
  /// <see cref="DetectionTarget.ReferenceImageId"/> holds the first item.
  /// </summary>
  /// <param name="dto">The target. It must pass <see cref="HasImage"/>.</param>
  /// <param name="confidence">The confidence.</param>
  /// <param name="offsetX">The x offset.</param>
  /// <param name="offsetY">The y offset.</param>
  /// <param name="selection">The selection strategy.</param>
  public static DetectionTarget ToDomainWait(
    DetectionTargetDto dto, double confidence, int offsetX, int offsetY, DetectionSelectionStrategy selection) {
    IReadOnlyList<string>? list = null;
    var id = dto.ReferenceImageId;
    if (dto.ReferenceImageIds is not null) {
      var normalized = ImageListRules.Normalize(dto.ReferenceImageIds);
      if (normalized.Count > 0) {
        list = normalized;
        if (string.IsNullOrWhiteSpace(id)) id = normalized[0];
      }
    }

    return new DetectionTarget(id!, confidence, offsetX, offsetY, selection, PixelRegionDto.ToDomain(dto.Region), list);
  }

  /// <summary>Copies the list of a domain target to the response. A target with a list has no single id in the response.</summary>
  /// <param name="detection">The domain target.</param>
  public static (string? Id, Collection<string>? Ids) ToResponse(DetectionTarget detection) =>
    detection.ReferenceImageIds is { Count: > 0 } ids
      ? (null, new Collection<string>(ids.ToList()))
      : (detection.ReferenceImageId, null);
}
