using System;
using System.Threading;
using System.Threading.Tasks;
using GameBot.Domain.Commands;
using GameBot.Domain.Services;
using GameBot.Domain.Triggers;

namespace GameBot.Service.Services.Conditions;

internal interface IImageDetectionConditionAdapter {
  ValueTask<bool> EvaluateAsync(ConditionOperand operand, CancellationToken cancellationToken = default);
}

internal sealed class ImageDetectionConditionAdapter : IImageDetectionConditionAdapter {
  private readonly TriggerEvaluationService _triggerEvaluationService;

  public ImageDetectionConditionAdapter(TriggerEvaluationService triggerEvaluationService) {
    _triggerEvaluationService = triggerEvaluationService;
  }

  public ValueTask<bool> EvaluateAsync(ConditionOperand operand, CancellationToken cancellationToken = default) {
    ArgumentNullException.ThrowIfNull(operand);

    cancellationToken.ThrowIfCancellationRequested();

    if (operand.OperandType != ConditionOperandType.ImageDetection) {
      return ValueTask.FromResult(false);
    }

    var threshold = operand.Threshold ?? 0.85;
    var trigger = new Trigger {
      Id = "inline-image-condition",
      Type = TriggerType.ImageMatch,
      Enabled = true,
      Params = new ImageMatchParams {
        ReferenceImageId = operand.TargetRef,
        Region = new Region {
          X = 0,
          Y = 0,
          Width = 1,
          Height = 1
        },
        SimilarityThreshold = threshold
      }
    };

    var evaluation = _triggerEvaluationService.Evaluate(trigger, DateTimeOffset.UtcNow);
    var hasMatch = evaluation.Status == TriggerStatus.Satisfied;

    var expectedPresent = string.Equals(operand.ExpectedState, "present", StringComparison.OrdinalIgnoreCase)
                          || string.Equals(operand.ExpectedState, "success", StringComparison.OrdinalIgnoreCase)
                          || string.IsNullOrWhiteSpace(operand.ExpectedState);

    var result = expectedPresent ? hasMatch : !hasMatch;
    return ValueTask.FromResult(result);
  }
}
