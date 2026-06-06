using GameBot.Domain.Commands;

namespace GameBot.Service.Services;

/// <summary>Extracts image reference IDs from commands and sequence steps.</summary>
internal static class ImageReferenceExtractor {
  /// <summary>Returns all image IDs referenced by a command's detection target and steps.</summary>
  public static IEnumerable<string> ExtractImageIds(Command command) {
    if (command.Detection?.ReferenceImageId is { Length: > 0 } detectionId)
      yield return detectionId;

    foreach (var step in command.Steps) {
      if (step.Type == CommandStepType.PrimitiveTap) {
        var id = step.PrimitiveTap?.DetectionTarget?.ReferenceImageId;
        if (!string.IsNullOrWhiteSpace(id)) yield return id;
      }
      else if (step.Type == CommandStepType.WaitForImage) {
        var id = step.WaitForImage?.DetectionTarget?.ReferenceImageId;
        if (!string.IsNullOrWhiteSpace(id)) yield return id;
      }
    }
  }

  /// <summary>Returns all image IDs referenced by a list of sequence steps, recursing into loop bodies.</summary>
  public static IEnumerable<string> ExtractImageIds(IEnumerable<SequenceStep> steps) {
    foreach (var step in steps) {
      var gateId = step.Gate?.TargetId;
      if (!string.IsNullOrWhiteSpace(gateId)) yield return gateId;

      var waitId = step.WaitForImage?.DetectionTarget?.ReferenceImageId;
      if (!string.IsNullOrWhiteSpace(waitId)) yield return waitId;

      if (step.StepType == SequenceStepType.Loop && step.Body.Count > 0) {
        foreach (var id in ExtractImageIds(step.Body))
          yield return id;
      }
    }
  }
}
