using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using GameBot.Domain.Actions;
using GameBot.Domain.Commands;
using GameBot.Domain.Parameters;
using GameBot.Domain.Services;
using Xunit;

// Test-code analyzer relaxations (permitted by the constitution for test code).
#pragma warning disable CA2007, CA1861, CA1859

namespace GameBot.UnitTests.Sequences;

/// <summary>
/// Feature 115 (issue #246): the runner resolves a placeholder in a step <c>parameterBindings</c>
/// value against the step scope before it calls the command dispatcher.
/// </summary>
public sealed class SequenceRunnerBindingScopeTests {
  private const string Name = "novaOptionImage";
  private const string EntryValue = "pns-alliance-nav-button";

  private const string UnresolvedMessage =
      "Step 's1': parameter 'novaOptionImage' used by field 'parameterBindings.novaOptionImage' could not be resolved from any scope. "
      + "Do one of these to supply a value for 'novaOptionImage'. Supply the value in the queue template entry or in the run request. "
      + "Give 'novaOptionImage' a default value in the sequence or in the calling command. "
      + "Bind a literal value, or bind a value in the calling command.";

  private sealed class StubRepo : ISequenceRepository {
    private readonly CommandSequence _sequence;
    public StubRepo(CommandSequence sequence) => _sequence = sequence;
    public Task<CommandSequence?> GetAsync(string id) => Task.FromResult<CommandSequence?>(_sequence);
    public Task<IReadOnlyList<CommandSequence>> ListAsync() => Task.FromResult<IReadOnlyList<CommandSequence>>(new List<CommandSequence> { _sequence });
    public Task<CommandSequence> CreateAsync(CommandSequence sequence) => Task.FromResult(sequence);
    public Task<CommandSequence> UpdateAsync(CommandSequence sequence) => Task.FromResult(sequence);
    public Task<bool> DeleteAsync(string id) => Task.FromResult(true);
  }

  /// <summary>What one run did: the result, the scopes the dispatcher got, the guard image ids, and the device inputs.</summary>
  private sealed record RunRecord(
      SequenceExecutionResult Result,
      List<ParameterScope> Scopes,
      List<string> ImageIds,
      int ActionDispatches);

  private static Collection<ParameterBinding> Bind(string name, string? value) =>
      new() { new ParameterBinding { Name = name, Value = value } };

  private static SequenceStep CommandStep(
      string stepId,
      Collection<ParameterBinding>? bindings,
      int order = 0,
      SequenceStepCondition? condition = null) => new() {
        Order = order,
        StepId = stepId,
        StepType = SequenceStepType.Command,
        CommandId = "ZZZ.NovaParamTap",
        ParameterBindings = bindings,
        Condition = condition
      };

  private static SequenceStep Tap(string stepId, int order) => new() {
    Order = order,
    StepId = stepId,
    StepType = SequenceStepType.Action,
    Action = new SequenceActionPayload { Type = ActionTypes.Tap, Parameters = { ["x"] = 1, ["y"] = 2 } }
  };

  private static ParameterScope EntryScope(string? value) {
    var bindings = new Collection<ParameterBinding>();
    if (value is not null) bindings.Add(new ParameterBinding { Name = Name, Value = value });
    return ParameterScope.Empty.Child(ParameterScopeLayers.Entry, bindings, null);
  }

  private static async Task<RunRecord> RunAsync(
      IEnumerable<SequenceStep> steps,
      ParameterScope scope,
      bool dryRun = false,
      params ParameterDeclaration[] declarations) {
    var sequence = new CommandSequence { Id = "seq", Name = "Seq" };
    foreach (var declaration in declarations) sequence.Parameters.Add(declaration);
    sequence.SetSteps(steps.ToArray());
    var runner = new SequenceRunner(new StubRepo(sequence));
    var scopes = new List<ParameterScope>();
    var imageIds = new List<string>();
    var dispatches = 0;

    var result = await runner.ExecuteAsync(
      sequence.Id,
      (_, _) => Task.CompletedTask,
      commandDispatcher: (_, commandScope) => {
        scopes.Add(commandScope);
        return Task.FromResult(dryRun
            ? new CommandDispatchOutcome(false, null, SkippedDryRun: true)
            : CommandDispatchOutcome.Executed);
      },
      conditionEvaluator: (condition, _) => {
        imageIds.Add(condition.TargetId ?? string.Empty);
        return Task.FromResult(true);
      },
      actionDispatcher: (_, _) => {
        dispatches++;
        return Task.FromResult(new ActionDispatchResult("executed", null));
      },
      scope: scope,
      dryRun: dryRun,
      ct: CancellationToken.None);

    return new RunRecord(result, scopes, imageIds, dispatches);
  }

  private static ParameterValue Resolve(ParameterScope scope, string name) {
    scope.TryResolve(name, out var value).Should().BeTrue();
    return value;
  }

  // ── US1: a binding placeholder gets the value of the sequence scope ────────

  [Fact]
  public async Task WholePlaceholderBindingGetsTheEntryValueAndLayer() {
    var run = await RunAsync(new[] { CommandStep("s1", Bind(Name, "{{novaOptionImage}}")) }, EntryScope(EntryValue));

    var value = Resolve(run.Scopes.Should().ContainSingle().Subject, Name);
    value.Text.Should().Be(EntryValue);
    value.OriginLayer.Should().Be(ParameterScopeLayers.Entry);
    run.Result.Status.Should().Be("Succeeded");
  }

  [Fact]
  public async Task StepGuardAndBindingOfTheSameStepUseTheSameValue() {
    var guard = new ImageVisibleStepCondition { ImageId = "{{novaOptionImage}}" };
    var run = await RunAsync(
        new[] { CommandStep("s1", Bind(Name, "{{novaOptionImage}}"), condition: guard) }, EntryScope(EntryValue));

    run.ImageIds.Should().Equal(EntryValue);
    Resolve(run.Scopes.Should().ContainSingle().Subject, Name).Text.Should().Be(EntryValue);
  }

  [Fact]
  public async Task IterationPlaceholderInALoopBodyBindingGetsTheLoopValue() {
    var loop = new SequenceStep {
      Order = 0, StepId = "loop1", StepType = SequenceStepType.Loop,
      Loop = new CountLoopConfig { Count = 2 },
      Body = new[] { CommandStep("body", Bind("n", "{{iteration}}")) }
    };

    var run = await RunAsync(new[] { loop }, EntryScope(null));

    run.Scopes.Should().HaveCount(2);
    run.Scopes.Select(s => Resolve(s, "n")).Should().Equal(
        new ParameterValue("1", ParameterScopeLayers.Loop),
        new ParameterValue("2", ParameterScopeLayers.Loop));
  }

  [Fact]
  public async Task DryRunGivesTheSameResolution() {
    var run = await RunAsync(
        new[] { CommandStep("s1", Bind(Name, "{{novaOptionImage}}")) }, EntryScope(EntryValue), dryRun: true);

    var value = Resolve(run.Scopes.Should().ContainSingle().Subject, Name);
    value.Text.Should().Be(EntryValue);
    value.OriginLayer.Should().Be(ParameterScopeLayers.Entry);
  }

  // ── US2: the current binding forms do not change ───────────────────────────

  [Fact]
  public async Task StepWithNoBindingsInheritsTheEntryValue() {
    var run = await RunAsync(new[] { CommandStep("s1", null) }, EntryScope(EntryValue));

    var value = Resolve(run.Scopes.Should().ContainSingle().Subject, Name);
    value.Text.Should().Be(EntryValue);
    value.OriginLayer.Should().Be(ParameterScopeLayers.Entry);
  }

  [Fact]
  public async Task LiteralBindingKeepsTheCommandLayer() {
    var run = await RunAsync(new[] { CommandStep("s1", Bind(Name, "pns-todo-radar")) }, EntryScope(EntryValue));

    var value = Resolve(run.Scopes.Should().ContainSingle().Subject, Name);
    value.Text.Should().Be("pns-todo-radar");
    value.OriginLayer.Should().Be(ParameterScopeLayers.Command);
  }

  [Fact]
  public async Task NullBindingInheritsTheOuterValueAndLayer() {
    var run = await RunAsync(new[] { CommandStep("s1", Bind(Name, null)) }, EntryScope(EntryValue));

    var value = Resolve(run.Scopes.Should().ContainSingle().Subject, Name);
    value.Text.Should().Be(EntryValue);
    value.OriginLayer.Should().Be(ParameterScopeLayers.Entry);
  }

  [Fact]
  public async Task DollarBraceBindingStaysLiteral() {
    var run = await RunAsync(new[] { CommandStep("s1", Bind(Name, "${novaOptionImage}")) }, EntryScope(EntryValue));

    var value = Resolve(run.Scopes.Should().ContainSingle().Subject, Name);
    value.Text.Should().Be("${novaOptionImage}");
    value.OriginLayer.Should().Be(ParameterScopeLayers.Command);
  }

  // ── US3: an unresolved binding placeholder fails clearly ───────────────────

  private static readonly ParameterDeclaration OptionalNoDefault = new() { Name = Name, Required = false };

  [Theory]
  [InlineData(false)]
  [InlineData(true)]
  public async Task UnresolvedBindingFailsTheStepAndStopsTheRun(bool dryRun) {
    var steps = new[] { CommandStep("s1", Bind(Name, "{{novaOptionImage}}")), Tap("later", 1) };

    var run = await RunAsync(steps, EntryScope(null), dryRun, OptionalNoDefault);

    run.Result.Status.Should().Be("Failed");
    var failed = run.Result.Steps.Should().ContainSingle().Subject;
    failed.Status.Should().Be("Failed");
    failed.ActionOutcome.Should().Be("failed");
    failed.Message.Should().Be(UnresolvedMessage);
    run.Scopes.Should().BeEmpty("the command dispatcher is not called");
    run.ActionDispatches.Should().Be(0, "a later step does not run");
  }
}
