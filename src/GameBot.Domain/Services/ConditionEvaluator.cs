using System;
using System.Threading;
using System.Threading.Tasks;
using GameBot.Domain.Commands;

namespace GameBot.Domain.Services;

public interface IConditionEvaluator
{
    ValueTask<bool> EvaluateAsync(
        ConditionExpression expression,
        Func<ConditionOperand, CancellationToken, ValueTask<bool>> operandEvaluator,
        CancellationToken cancellationToken = default);
}

public sealed class ConditionEvaluator : IConditionEvaluator
{
    public async ValueTask<bool> EvaluateAsync(
        ConditionExpression expression,
        Func<ConditionOperand, CancellationToken, ValueTask<bool>> operandEvaluator,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(expression);
        ArgumentNullException.ThrowIfNull(operandEvaluator);

        cancellationToken.ThrowIfCancellationRequested();

        return expression.NodeType switch
        {
            ConditionNodeType.And => await EvaluateAndAsync(expression, operandEvaluator, cancellationToken).ConfigureAwait(false),
            ConditionNodeType.Or => await EvaluateOrAsync(expression, operandEvaluator, cancellationToken).ConfigureAwait(false),
            ConditionNodeType.Not => await EvaluateNotAsync(expression, operandEvaluator, cancellationToken).ConfigureAwait(false),
            ConditionNodeType.Operand => await EvaluateOperandAsync(expression, operandEvaluator, cancellationToken).ConfigureAwait(false),
            _ => throw new InvalidOperationException($"Unsupported condition node type '{expression.NodeType}'.")
        };
    }

    private async ValueTask<bool> EvaluateAndAsync(
        ConditionExpression expression,
        Func<ConditionOperand, CancellationToken, ValueTask<bool>> operandEvaluator,
        CancellationToken cancellationToken)
    {
        if (expression.Children.Count < 2)
        {
            throw new InvalidOperationException("AND node requires at least two children.");
        }

        foreach (var child in expression.Children)
        {
            if (!await EvaluateAsync(child, operandEvaluator, cancellationToken).ConfigureAwait(false))
            {
                return false;
            }
        }

        return true;
    }

    private async ValueTask<bool> EvaluateOrAsync(
        ConditionExpression expression,
        Func<ConditionOperand, CancellationToken, ValueTask<bool>> operandEvaluator,
        CancellationToken cancellationToken)
    {
        if (expression.Children.Count < 2)
        {
            throw new InvalidOperationException("OR node requires at least two children.");
        }

        foreach (var child in expression.Children)
        {
            if (await EvaluateAsync(child, operandEvaluator, cancellationToken).ConfigureAwait(false))
            {
                return true;
            }
        }

        return false;
    }

    private async ValueTask<bool> EvaluateNotAsync(
        ConditionExpression expression,
        Func<ConditionOperand, CancellationToken, ValueTask<bool>> operandEvaluator,
        CancellationToken cancellationToken)
    {
        if (expression.Children.Count != 1)
        {
            throw new InvalidOperationException("NOT node requires exactly one child.");
        }

        return !await EvaluateAsync(expression.Children[0], operandEvaluator, cancellationToken).ConfigureAwait(false);
    }

    private static ValueTask<bool> EvaluateOperandAsync(
        ConditionExpression expression,
        Func<ConditionOperand, CancellationToken, ValueTask<bool>> operandEvaluator,
        CancellationToken cancellationToken)
    {
        if (expression.Operand is null)
        {
            throw new InvalidOperationException("Operand node requires operand metadata.");
        }

        return operandEvaluator(expression.Operand, cancellationToken);
    }
}