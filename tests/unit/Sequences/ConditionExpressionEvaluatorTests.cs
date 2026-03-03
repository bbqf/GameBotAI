using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using GameBot.Domain.Commands;
using GameBot.Domain.Services;
using Xunit;

namespace GameBot.UnitTests.Sequences;

public sealed class ConditionExpressionEvaluatorTests {
  [Fact]
  public async Task EvaluateAsyncUsesLeftToRightShortCircuitForAndAndOr() {
    var evaluator = new ConditionEvaluator();
    var visited = new List<string>();

    var expression = new ConditionExpression {
      NodeType = ConditionNodeType.Or
    };

    var andNode = new ConditionExpression {
      NodeType = ConditionNodeType.And
    };
    andNode.SetChildren(new[] {
      Operand("a"),
      Operand("b"),
      Operand("c")
    });

    expression.SetChildren(new[] {
      andNode,
      Operand("d")
    });

    var result = await evaluator.EvaluateAsync(
      expression,
      (operand, _) => {
        visited.Add(operand.TargetRef);
        return new ValueTask<bool>(operand.TargetRef switch {
          "a" => true,
          "b" => false,
          "c" => true,
          "d" => true,
          _ => false
        });
      },
      CancellationToken.None);

    result.Should().BeTrue();
    visited.Should().Equal("a", "b", "d");
  }

  [Fact]
  public async Task EvaluateAsyncAppliesNotToChildExpression() {
    var evaluator = new ConditionEvaluator();
    var expression = new ConditionExpression {
      NodeType = ConditionNodeType.Not
    };
    expression.SetChildren(new[] { Operand("negated") });

    var result = await evaluator.EvaluateAsync(
      expression,
      (_, _) => new ValueTask<bool>(true),
      CancellationToken.None);

    result.Should().BeFalse();
  }

  private static ConditionExpression Operand(string targetRef) => new() {
    NodeType = ConditionNodeType.Operand,
    Operand = new ConditionOperand {
      OperandType = ConditionOperandType.CommandOutcome,
      TargetRef = targetRef,
      ExpectedState = "success"
    }
  };
}