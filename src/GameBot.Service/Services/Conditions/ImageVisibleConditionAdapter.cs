using System;
using System.Threading;
using System.Threading.Tasks;
using GameBot.Domain.Commands.Blocks;
using GameBot.Domain.Services;
using GameBot.Domain.Triggers;

namespace GameBot.Service.Services.Conditions;

internal interface IImageVisibleConditionAdapter {
  ValueTask<bool> EvaluateAsync(Condition condition, CancellationToken cancellationToken = default);
}

internal sealed class ImageVisibleConditionAdapter : IImageVisibleConditionAdapter {
  private readonly TriggerEvaluationService _triggerEvaluationService;

  public ImageVisibleConditionAdapter(TriggerEvaluationService triggerEvaluationService) {
    _triggerEvaluationService = triggerEvaluationService;
  }

  public ValueTask<bool> EvaluateAsync(Condition condition, CancellationToken cancellationToken = default) {
    ArgumentNullException.ThrowIfNull(condition);

    cancellationToken.ThrowIfCancellationRequested();

    var region = condition.Region is null
      ? new Region { X = 0, Y = 0, Width = 1, Height = 1 }
      : new Region {
        X = condition.Region.X,
        Y = condition.Region.Y,
        Width = condition.Region.Width,
        Height = condition.Region.Height
      };

    var trigger = new Trigger {
      Id = "inline-image-visible",
      Type = TriggerType.ImageMatch,
      Enabled = true,
      Params = new ImageMatchParams {
        ReferenceImageId = condition.TargetId,
        Region = region,
        SimilarityThreshold = condition.ConfidenceThreshold ?? 0.85
      }
    };

    var evaluation = _triggerEvaluationService.Evaluate(trigger, DateTimeOffset.UtcNow);
    var isVisible = evaluation.Status == TriggerStatus.Satisfied;
    var expectedPresent = !string.Equals(condition.Mode, "Absent", StringComparison.OrdinalIgnoreCase);

    return ValueTask.FromResult(expectedPresent ? isVisible : !isVisible);
  }
}
