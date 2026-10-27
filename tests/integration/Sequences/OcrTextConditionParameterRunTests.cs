using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using GameBot.Domain.Commands;
using GameBot.Domain.Parameters;
using GameBot.Domain.Services;
using GameBot.Domain.Services.StepThrough;
using GameBot.Service.Services.SequenceExecution;
using Xunit;
using static GameBot.IntegrationTests.Sequences.OcrTextRunKit;

#pragma warning disable CA2007, CA1826, CA1861

namespace GameBot.IntegrationTests.Sequences;

/// <summary>
/// Feature 165: sequence parameters in <c>ocrText.pattern</c> and <c>ocrText.expected</c> at run time,
/// in each condition place, in the run log, and in a step-through.
/// </summary>
public sealed class OcrTextConditionParameterRunTests {
  private const string TwoGatherings = "Gathering 1\nGathering 2";
  private const string MarchPattern = "(?s)(Gathering.*){{{marchTarget}}}";

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

  private static CommandSequence Sequence(params SequenceStep[] steps) {
    var sequence = new CommandSequence { Id = "seq", Name = "seq" };
    sequence.SetSteps(steps);
    return sequence;
  }

  private static ParameterScope Values(params (string Name, string Value)[] values) =>
      ParameterScope.Empty.Child(
          ParameterScopeLayers.Entry,
          values.Select(v => new ParameterBinding { Name = v.Name, Value = v.Value }),
          null);

  /// <summary>Runs <paramref name="sequence"/> with the supplied values and an OCR text.</summary>
  private static async Task<(SequenceExecutionResult Result, List<string> Executed)> RunAsync(
      CommandSequence sequence, string screenText, params (string Name, string Value)[] values) {
    var executed = new List<string>();
    using var ocr = SequenceExecutionService.PushOcrScope(new FakeReader { Text = screenText }, "sess-1", false);
    var result = await new SequenceRunner(new StubRepo(sequence)).ExecuteAsync(
        "seq",
        (commandId, _) => { executed.Add(commandId); return Task.CompletedTask; },
        conditionEvaluator: (_, _) => Task.FromResult(false),
        scope: Values(values),
        ct: CancellationToken.None);
    return (result, executed);
  }

  private static SequenceStep WhileLoop(SequenceStepCondition condition) => new() {
    Order = 0,
    StepId = "loop",
    StepType = SequenceStepType.Loop,
    Loop = new WhileLoopConfig { Condition = condition, MaxIterations = 1, ExitOnMaxIterations = true },
    Body = new[] { Cmd(0, "body") }
  };

  private static SequenceStep BreakLoop(SequenceStepCondition breakCondition) => new() {
    Order = 0,
    StepId = "loop",
    StepType = SequenceStepType.Loop,
    Loop = new CountLoopConfig { Count = 3 },
    Body = new[] {
      Cmd(0, "body"),
      new SequenceStep { Order = 1, StepId = "brk", StepType = SequenceStepType.Break, BreakCondition = breakCondition }
    }
  };

  private static string AllMessages(SequenceExecutionResult result) =>
      string.Join("\n", result.Steps.Select(s => s.Message + " " + s.Error));

  // ── User Story 1: pattern ──────────────────────────────────────────────────

  [Fact]
  public async Task AStepConditionPatternResolvesToTheValue() {
    var (_, two) = await RunAsync(Sequence(Cmd(0, "gather", Match(MarchPattern))), TwoGatherings, ("marchTarget", "2"));
    var (_, three) = await RunAsync(Sequence(Cmd(0, "gather", Match(MarchPattern))), TwoGatherings, ("marchTarget", "3"));

    two.Should().Equal("gather");
    three.Should().BeEmpty();
  }

  [Fact]
  public async Task AnIfConditionPatternResolves() {
    var (_, two) = await RunAsync(Sequence(If(0, "check", Match(MarchPattern))), TwoGatherings, ("marchTarget", "2"));
    var (_, three) = await RunAsync(Sequence(If(0, "check", Match(MarchPattern))), TwoGatherings, ("marchTarget", "3"));

    two.Should().Equal("then");
    three.Should().Equal("else");
  }

  [Fact]
  public async Task ALoopConditionPatternResolves() {
    var (_, two) = await RunAsync(Sequence(WhileLoop(Match(MarchPattern))), TwoGatherings, ("marchTarget", "2"));
    var (_, three) = await RunAsync(Sequence(WhileLoop(Match(MarchPattern))), TwoGatherings, ("marchTarget", "3"));

    two.Should().Equal("body");
    three.Should().BeEmpty();
  }

  [Fact]
  public async Task ABreakConditionPatternResolves() {
    var (_, two) = await RunAsync(Sequence(BreakLoop(Match(MarchPattern))), TwoGatherings, ("marchTarget", "2"));
    var (_, three) = await RunAsync(Sequence(BreakLoop(Match(MarchPattern))), TwoGatherings, ("marchTarget", "3"));

    two.Should().Equal("body");
    three.Should().Equal("body", "body", "body");
  }

  [Fact]
  public async Task IterationInALoopConditionPatternResolves() {
    var (result, executed) = await RunAsync(Sequence(WhileLoop(Match("Round {{iteration}}$"))), "Round 1");

    result.Status.Should().Be("Succeeded");
    executed.Should().Equal("body");
  }

  [Fact]
  public async Task AnInvalidResolvedPatternFailsTheStep() {
    var (result, executed) = await RunAsync(
        Sequence(Cmd(0, "gather", Match("{{countPattern}}"))), TwoGatherings, ("countPattern", "("));

    result.Status.Should().Be("Failed");
    executed.Should().BeEmpty();
    AllMessages(result).Should().Contain(
        "Step 'gather': field 'condition.pattern' resolved to the pattern '(', which is not a valid regular expression. "
        + "Change the value of 'countPattern' or the pattern.");
  }

  [Fact]
  public async Task AWhiteSpaceFullPatternFailsAsUnresolved() {
    var (result, _) = await RunAsync(
        Sequence(Cmd(0, "gather", Match("{{countPattern}}"))), TwoGatherings, ("countPattern", " "));

    result.Status.Should().Be("Failed");
    AllMessages(result).Should().Contain(
        "Step 'gather': parameter 'countPattern' used by field 'condition.pattern' could not be resolved from any scope.");
  }

  [Fact]
  public async Task AnUnknownNameFailsAsUnresolved() {
    var (result, _) = await RunAsync(Sequence(Cmd(0, "gather", Match(MarchPattern))), TwoGatherings);

    result.Status.Should().Be("Failed");
    AllMessages(result).Should().Contain(
        "Step 'gather': parameter 'marchTarget' used by field 'condition.pattern' could not be resolved from any scope.");
  }

  [Fact]
  public async Task DoubleBraceDigitsWithNoParameterFailsAndTheReaderGetsNoPlaceholder() {
    var reader = new FakeReader { Text = TwoGatherings };
    var sequence = Sequence(Cmd(0, "gather", Match("(Gathering.*){{3}}")));
    SequenceExecutionResult result;
    using (SequenceExecutionService.PushOcrScope(reader, "sess-1", false)) {
      result = await new SequenceRunner(new StubRepo(sequence)).ExecuteAsync(
          "seq", (_, _) => Task.CompletedTask, conditionEvaluator: (_, _) => Task.FromResult(false));
    }

    result.Status.Should().Be("Failed");
    AllMessages(result).Should().Contain("parameter '3' used by field 'condition.pattern'");
    reader.Sessions.Should().BeEmpty("the OCR check does not get the text {{3}}");
  }

  [Fact]
  public async Task ABreakConditionWithAnUnknownNameGivesNoBreak() {
    var (result, executed) = await RunAsync(Sequence(BreakLoop(Match(MarchPattern))), TwoGatherings);

    result.Status.Should().Be("Succeeded");
    executed.Should().Equal("body", "body", "body");
    var breakStep = result.Steps.First(s => s.CommandId == "brk");
    breakStep.Message.Should().StartWith("No break:").And.Contain("parameter 'marchTarget'");
  }

  [Fact]
  public async Task ABreakConditionWithAnInvalidResolvedPatternGivesNoBreak() {
    var (result, executed) = await RunAsync(
        Sequence(BreakLoop(Match("{{countPattern}}"))), TwoGatherings, ("countPattern", "("));

    result.Status.Should().Be("Succeeded");
    executed.Should().Equal("body", "body", "body");
    result.Steps.First(s => s.CommandId == "brk").Message
        .Should().StartWith("No break:").And.Contain("is not a valid regular expression");
  }

  [Fact]
  public async Task TheStoredSequenceKeepsItsPlaceholderText() {
    var patternCondition = Match(MarchPattern);
    var expectedCondition = Expect("Level {{level}}");
    var sequence = Sequence(Cmd(0, "gather", patternCondition), If(1, "check", expectedCondition));

    await RunAsync(sequence, TwoGatherings, ("marchTarget", "2"), ("level", "5"));

    patternCondition.Pattern.Should().Be(MarchPattern);
    expectedCondition.Expected.Should().Be("Level {{level}}");
    sequence.Steps[0].Condition.Should().BeSameAs(patternCondition);
  }

  // ── User Story 2: expected ─────────────────────────────────────────────────

  [Fact]
  public async Task AFullExpectedFieldResolves() {
    var (_, executed) = await RunAsync(Sequence(If(0, "check", Expect("{{accountName}}"))), "Alpha", ("accountName", "Alpha"));

    executed.Should().Equal("then");
  }

  [Fact]
  public async Task AnInlineExpectedFieldResolves() {
    var (_, same) = await RunAsync(Sequence(If(0, "check", Expect("Level {{level}}"))), "Level 5", ("level", "5"));
    var (_, other) = await RunAsync(Sequence(If(0, "check", Expect("Level {{level}}"))), "Level 6", ("level", "5"));

    same.Should().Equal("then");
    other.Should().Equal("else");
  }

  // ── User Story 3: run log ──────────────────────────────────────────────────

  [Fact]
  public async Task AStepConditionLogShowsTheResolvedPattern() {
    var guard = new AllStepCondition { Children = new SequenceStepCondition[] { Match(MarchPattern) } };

    var (result, _) = await RunAsync(Sequence(Cmd(0, "gather", guard)), TwoGatherings, ("marchTarget", "4"));

    var message = result.Steps.Single().Message!;
    message.Should().Contain("pattern=(?s)(Gathering.*){4}");
    message.Should().Contain("ocrText: read=").And.Contain("pattern='(?s)(Gathering.*){4}' result=false");
    message.Should().NotContain("{{");
  }

  [Fact]
  public async Task AnIfConditionLogShowsTheResolvedPattern() {
    var (result, _) = await RunAsync(Sequence(If(0, "check", Match(MarchPattern))), TwoGatherings, ("marchTarget", "4"));

    var message = result.Steps.First().Message!;
    message.Should().Contain("pattern=(?s)(Gathering.*){4}");
    message.Should().Contain("pattern='(?s)(Gathering.*){4}' result=false");
    AllMessages(result).Should().NotContain("{{");
  }

  [Fact]
  public async Task ABreakConditionLogShowsTheResolvedPattern() {
    var (result, _) = await RunAsync(Sequence(BreakLoop(Match(MarchPattern))), TwoGatherings, ("marchTarget", "4"));

    var message = result.Steps.First(s => s.CommandId == "brk").Message!;
    message.Should().Contain("pattern=(?s)(Gathering.*){4}");
    message.Should().Contain("pattern='(?s)(Gathering.*){4}' result=false");
    AllMessages(result).Should().NotContain("{{");
  }

  [Fact]
  public async Task AnExpectedLogShowsTheResolvedValue() {
    var (result, _) = await RunAsync(Sequence(If(0, "check", Expect("Level {{level}}"))), "Level 6", ("level", "5"));

    var message = result.Steps.First().Message!;
    message.Should().Contain("expected='Level 5'");
    message.Should().NotContain("{{");
  }

  // ── Step-through (FR-010) ──────────────────────────────────────────────────

  private static async Task<List<HistoryEntry>> StepThroughAsync(
      CommandSequence sequence, params (string Name, string Value)[] values) {
    var state = new StepperState();
    state.Restart(sequence);
    foreach (var (name, value) in values) state.ParameterValues[name] = value;
    var stepper = new SequenceStepper(new SequenceRunner(new StubRepo(sequence)));
    var dependencies = new StepperDependencies {
      ExecuteCommandAsync = (_, _) => Task.CompletedTask,
      ConditionEvaluator = (_, _) => Task.FromResult(false)
    };
    var entries = new List<HistoryEntry>();
    for (var i = 0; i < 50 && state.Cursor is not null; i++) {
      entries.AddRange(await stepper.RunNextAsync(sequence, state, dependencies));
    }

    return entries;
  }

  [Fact]
  public async Task AStepThroughResolvesTheSameWay() {
    var entries = await StepThroughAsync(Sequence(If(0, "check", Match(MarchPattern))), ("marchTarget", "4"));

    var ifEntry = entries.First(e => e.StepId == "check");
    ifEntry.Status.Should().Be("Succeeded");
    ifEntry.Message.Should().Contain("pattern=(?s)(Gathering.*){4}").And.NotContain("{{");
  }

  [Fact]
  public async Task AStepThroughWithABadValueFailsWithTheRunMessage() {
    const string runMessage =
        "Step 'gather': field 'condition.pattern' resolved to the pattern '(', which is not a valid regular expression. "
        + "Change the value of 'countPattern' or the pattern.";
    var sequence = Sequence(Cmd(0, "gather", Match("{{countPattern}}")));

    var entries = await StepThroughAsync(sequence, ("countPattern", "("));
    var (result, _) = await RunAsync(sequence, TwoGatherings, ("countPattern", "("));

    var entry = entries.First(e => e.StepId == "gather");
    entry.Status.Should().Be("Failed");
    entry.Message.Should().Contain(runMessage);
    AllMessages(result).Should().Contain(runMessage);
  }
}
