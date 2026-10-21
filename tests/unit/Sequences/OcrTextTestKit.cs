using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using GameBot.Domain.Commands;
using GameBot.Domain.Services;

namespace GameBot.UnitTests.Sequences;

/// <summary>Helpers for the tests of the ocrText condition (issue #338).</summary>
internal static class OcrTextTestKit {
  private sealed class StubRepo : ISequenceRepository {
    private readonly CommandSequence _sequence;

    public StubRepo(CommandSequence sequence) { _sequence = sequence; }

    public Task<CommandSequence?> GetAsync(string id) => Task.FromResult<CommandSequence?>(_sequence);

    public Task<IReadOnlyList<CommandSequence>> ListAsync()
      => Task.FromResult<IReadOnlyList<CommandSequence>>(new[] { _sequence });

    public Task<CommandSequence> CreateAsync(CommandSequence sequence) => Task.FromResult(sequence);

    public Task<CommandSequence> UpdateAsync(CommandSequence sequence) => Task.FromResult(sequence);

    public Task<bool> DeleteAsync(string id) => Task.FromResult(true);
  }

  public static OcrTextStepCondition Expect(string expected, bool negate = false, string? parser = null) => new() {
    Region = new PixelRegion(10, 20, 100, 40),
    Expected = expected,
    Parser = parser,
    Negate = negate
  };

  public static OcrTextStepCondition Match(string pattern, string? parser = null) => new() {
    Region = new PixelRegion(10, 20, 100, 40),
    Pattern = pattern,
    Parser = parser
  };

  /// <summary>A scope whose reader answers with the text. The list gets each condition that was read.</summary>
  public static OcrTextEvaluationScope TextScope(string text, List<OcrTextStepCondition>? reads = null, string? parsed = null)
    => new((condition, _) => {
      reads?.Add(condition);
      return Task.FromResult(new OcrTextReadResult(text, parsed, null));
    });

  /// <summary>A scope whose reader fails with the reason code.</summary>
  public static OcrTextEvaluationScope FailScope(string reason)
    => new((_, _) => Task.FromResult(new OcrTextReadResult(null, null, reason)));

  public static Task<ConditionEvaluation> EvaluateAsync(SequenceStepCondition condition)
    => SequenceStepConditionEvaluator.EvaluateAsync(condition, null, new Dictionary<string, string>(), CancellationToken.None);

  public static CommandSequence Sequence(params SequenceStep[] steps) {
    var sequence = new CommandSequence { Id = "seq", Name = "seq" };
    sequence.SetSteps(steps);
    return sequence;
  }

  public static SequenceRunner Runner(CommandSequence sequence) => new(new StubRepo(sequence));

  public static SequenceStep Cmd(int order, string commandId, SequenceStepCondition? guard = null) => new() {
    Order = order,
    StepId = commandId,
    CommandId = commandId,
    StepType = SequenceStepType.Action,
    Action = new SequenceActionPayload { Type = "command", Parameters = { ["commandId"] = commandId } },
    Condition = guard
  };

  public static SequenceStep If(int order, string id, SequenceStepCondition condition, string thenCommand, string elseCommand) => new() {
    Order = order,
    StepId = id,
    StepType = SequenceStepType.If,
    If = new IfConfig { Condition = condition },
    Body = new[] { Cmd(0, thenCommand) },
    ElseBody = new[] { Cmd(0, elseCommand) }
  };

  /// <summary>Runs the sequence and returns the ids of the commands that ran.</summary>
  public static async Task<(SequenceExecutionResult Result, List<string> Executed)> RunAsync(CommandSequence sequence)
  {
    var executed = new List<string>();
    var result = await Runner(sequence).ExecuteAsync(
      "seq",
      (commandId, _) => { executed.Add(commandId); return Task.CompletedTask; },
      conditionEvaluator: (_, _) => Task.FromResult(false),
      ct: CancellationToken.None).ConfigureAwait(false);
    return (result, executed);
  }
}
