using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using GameBot.Domain.Commands;
using GameBot.Domain.Services;
using Xunit;

namespace GameBot.UnitTests.Sequences;

public sealed class CommandOutcomeConditionTests {
  [Fact]
  public async Task EvaluateAsyncCommandOutcomeOperandReturnsTrueWhenStateMatches() {
    var evaluator = new ConditionEvaluator();
    var outcomes = new Dictionary<string, string> {
      ["command-1"] = "success"
    };

    var expression = new ConditionExpression {
      NodeType = ConditionNodeType.Operand,
      Operand = new ConditionOperand {
        OperandType = ConditionOperandType.CommandOutcome,
        TargetRef = "command-1",
        ExpectedState = "success"
      }
    };

    var result = await evaluator.EvaluateAsync(
      expression,
      (operand, _) => {
        operand.OperandType.Should().Be(ConditionOperandType.CommandOutcome);
        var matches = outcomes.TryGetValue(operand.TargetRef, out var actual)
                      && string.Equals(actual, operand.ExpectedState, System.StringComparison.OrdinalIgnoreCase);
        return new ValueTask<bool>(matches);
      },
      CancellationToken.None);

    result.Should().BeTrue();
  }

  [Fact]
  public async Task EvaluateAsyncCommandOutcomeOperandReturnsFalseWhenStateDiffers() {
    var evaluator = new ConditionEvaluator();
    var outcomes = new Dictionary<string, string> {
      ["command-1"] = "failed"
    };

    var expression = new ConditionExpression {
      NodeType = ConditionNodeType.Operand,
      Operand = new ConditionOperand {
        OperandType = ConditionOperandType.CommandOutcome,
        TargetRef = "command-1",
        ExpectedState = "success"
      }
    };

    var result = await evaluator.EvaluateAsync(
      expression,
      (operand, _) => {
        var matches = outcomes.TryGetValue(operand.TargetRef, out var actual)
                      && string.Equals(actual, operand.ExpectedState, System.StringComparison.OrdinalIgnoreCase);
        return new ValueTask<bool>(matches);
      },
      CancellationToken.None);

    result.Should().BeFalse();
  }
}