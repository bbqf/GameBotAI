using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using GameBot.Domain.Commands;
using GameBot.Domain.Services;
using GameBot.Service.Services.Ocr;
using GameBot.Service.Services.SequenceExecution;

namespace GameBot.IntegrationTests.Sequences;

/// <summary>Helpers for the run tests of the ocrText condition (issue #338).</summary>
internal static class OcrTextRunKit {
  /// <summary>A reader that answers with a fixed text, or with a failure reason. It records each session id.</summary>
  internal sealed class FakeReader : IOcrTextConditionReader {
    public string? Text { get; set; }
    public string? Reason { get; set; }
    public List<string> Sessions { get; } = new();

    public Task<OcrTextReadResult> ReadAsync(OcrTextStepCondition condition, string sessionId, CancellationToken ct) {
      Sessions.Add(sessionId);
      return Task.FromResult(new OcrTextReadResult(Reason is null ? Text : null, null, Reason));
    }
  }

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

  public static OcrTextStepCondition Expect(string expected, bool negate = false) => new() {
    Region = new PixelRegion(10, 20, 100, 40),
    Expected = expected,
    Negate = negate
  };

  public static OcrTextStepCondition Match(string pattern) => new() {
    Region = new PixelRegion(10, 20, 100, 40),
    Pattern = pattern
  };

  public static SequenceStep Cmd(int order, string commandId, SequenceStepCondition? guard = null) => new() {
    Order = order,
    StepId = commandId,
    CommandId = commandId,
    StepType = SequenceStepType.Action,
    Action = new SequenceActionPayload { Type = "command", Parameters = { ["commandId"] = commandId } },
    Condition = guard
  };

  public static SequenceStep If(int order, string id, SequenceStepCondition condition) => new() {
    Order = order,
    StepId = id,
    StepType = SequenceStepType.If,
    If = new IfConfig { Condition = condition },
    Body = new[] { Cmd(0, "then") },
    ElseBody = new[] { Cmd(0, "else") }
  };

  /// <summary>
  /// Runs the steps. A non-null reader and session id push the same scope that the run service pushes for a run
  /// with a session. Returns the result and the ids of the commands that ran.
  /// </summary>
  public static async Task<(SequenceExecutionResult Result, List<string> Executed)> RunAsync(
      FakeReader? reader, string? sessionId, bool dryRun, params SequenceStep[] steps) {
    var sequence = new CommandSequence { Id = "seq", Name = "seq" };
    sequence.SetSteps(steps);
    var executed = new List<string>();

    using var scope = SequenceExecutionService.PushOcrScope(reader, sessionId, dryRun);
    var result = await new SequenceRunner(new StubRepo(sequence)).ExecuteAsync(
      "seq",
      (commandId, _) => { executed.Add(commandId); return Task.CompletedTask; },
      conditionEvaluator: (_, _) => Task.FromResult(false),
      ct: CancellationToken.None).ConfigureAwait(false);
    return (result, executed);
  }
}
