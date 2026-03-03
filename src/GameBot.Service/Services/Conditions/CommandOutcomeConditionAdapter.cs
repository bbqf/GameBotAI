using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using GameBot.Domain.Commands;

namespace GameBot.Service.Services.Conditions;

internal interface ICommandOutcomeConditionAdapter {
  ValueTask<bool> EvaluateAsync(
    ConditionOperand operand,
    IReadOnlyDictionary<string, string> commandOutcomes,
    CancellationToken cancellationToken = default);
}

internal sealed class CommandOutcomeConditionAdapter : ICommandOutcomeConditionAdapter {
  public ValueTask<bool> EvaluateAsync(
    ConditionOperand operand,
    IReadOnlyDictionary<string, string> commandOutcomes,
    CancellationToken cancellationToken = default) {
    ArgumentNullException.ThrowIfNull(operand);
    ArgumentNullException.ThrowIfNull(commandOutcomes);

    cancellationToken.ThrowIfCancellationRequested();

    if (operand.OperandType != ConditionOperandType.CommandOutcome) {
      return ValueTask.FromResult(false);
    }

    if (!commandOutcomes.TryGetValue(operand.TargetRef, out var actualState)) {
      return ValueTask.FromResult(false);
    }

    var matches = string.Equals(actualState, operand.ExpectedState, StringComparison.OrdinalIgnoreCase);
    return ValueTask.FromResult(matches);
  }
}
