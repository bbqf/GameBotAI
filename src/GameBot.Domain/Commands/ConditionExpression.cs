using System;
using System.Collections.Generic;

namespace GameBot.Domain.Commands;

public enum ConditionNodeType
{
    And,
    Or,
    Not,
    Operand
}

public enum ConditionOperandType
{
    CommandOutcome,
    ImageDetection
}

public sealed class ConditionOperand
{
    public ConditionOperandType OperandType { get; set; }
    public string TargetRef { get; set; } = string.Empty;
    public string ExpectedState { get; set; } = string.Empty;
    public double? Threshold { get; set; }
}

public sealed class ConditionExpression
{
    private readonly List<ConditionExpression> _children = new();

    public ConditionNodeType NodeType { get; set; }
    public ConditionOperand? Operand { get; set; }
    public IReadOnlyList<ConditionExpression> Children => _children.AsReadOnly();

    public void SetChildren(IEnumerable<ConditionExpression>? children)
    {
        _children.Clear();
        if (children is null)
        {
            return;
        }

        _children.AddRange(children);
    }

    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();
        Validate(errors, "$", this);
        return errors;
    }

    private static void Validate(ICollection<string> errors, string path, ConditionExpression expression)
    {
        switch (expression.NodeType)
        {
            case ConditionNodeType.And:
            case ConditionNodeType.Or:
                if (expression._children.Count < 2)
                {
                    errors.Add($"{path}: '{expression.NodeType}' requires at least two children.");
                }

                if (expression.Operand is not null)
                {
                    errors.Add($"{path}: '{expression.NodeType}' node cannot define an operand.");
                }

                break;

            case ConditionNodeType.Not:
                if (expression._children.Count != 1)
                {
                    errors.Add($"{path}: 'Not' requires exactly one child.");
                }

                if (expression.Operand is not null)
                {
                    errors.Add($"{path}: 'Not' node cannot define an operand.");
                }

                break;

            case ConditionNodeType.Operand:
                if (expression._children.Count > 0)
                {
                    errors.Add($"{path}: operand node cannot contain child expressions.");
                }

                if (expression.Operand is null)
                {
                    errors.Add($"{path}: operand node requires operand metadata.");
                }

                break;

            default:
                throw new InvalidOperationException($"Unsupported condition node type '{expression.NodeType}'.");
        }

        for (var index = 0; index < expression._children.Count; index++)
        {
            Validate(errors, $"{path}.children[{index}]", expression._children[index]);
        }
    }
}