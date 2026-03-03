using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using GameBot.Domain.Commands;
using GameBot.Domain.Services;
using Xunit;

namespace GameBot.UnitTests.Sequences;

public sealed class ImageDetectionConditionTests {
  [Fact]
  public async Task EvaluateAsyncImageDetectionOperandTrueWhenAtLeastOneMatchMeetsThreshold() {
    var evaluator = new ConditionEvaluator();
    var scores = new Dictionary<string, double[]> {
      ["image-a"] = new[] { 0.62, 0.91, 0.74 }
    };

    var expression = new ConditionExpression {
      NodeType = ConditionNodeType.Operand,
      Operand = new ConditionOperand {
        OperandType = ConditionOperandType.ImageDetection,
        TargetRef = "image-a",
        ExpectedState = "present",
        Threshold = 0.85
      }
    };

    var result = await evaluator.EvaluateAsync(
      expression,
      (operand, _) => {
        operand.OperandType.Should().Be(ConditionOperandType.ImageDetection);
        var threshold = operand.Threshold ?? 1.0;
        var hasMatch = scores.TryGetValue(operand.TargetRef, out var values)
                       && values.Any(score => score >= threshold);
        return new ValueTask<bool>(hasMatch);
      },
      CancellationToken.None);

    result.Should().BeTrue();
  }

  [Fact]
  public async Task EvaluateAsyncImageDetectionOperandFalseWhenNoMatchMeetsThreshold() {
    var evaluator = new ConditionEvaluator();
    var scores = new Dictionary<string, double[]> {
      ["image-a"] = new[] { 0.40, 0.59, 0.79 }
    };

    var expression = new ConditionExpression {
      NodeType = ConditionNodeType.Operand,
      Operand = new ConditionOperand {
        OperandType = ConditionOperandType.ImageDetection,
        TargetRef = "image-a",
        ExpectedState = "present",
        Threshold = 0.80
      }
    };

    var result = await evaluator.EvaluateAsync(
      expression,
      (operand, _) => {
        var threshold = operand.Threshold ?? 1.0;
        var hasMatch = scores.TryGetValue(operand.TargetRef, out var values)
                       && values.Any(score => score >= threshold);
        return new ValueTask<bool>(hasMatch);
      },
      CancellationToken.None);

    result.Should().BeFalse();
  }
}