using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using GameBot.Domain.Commands;
using GameBot.Domain.Services;
using Xunit;
using Xunit.Abstractions;

namespace GameBot.IntegrationTests.Sequences;

public sealed class ConditionalExecutionPerformanceIntegrationTests {
  private readonly ITestOutputHelper _output;

  public ConditionalExecutionPerformanceIntegrationTests(ITestOutputHelper output) {
    _output = output;
  }

  [Fact]
  public async Task ConditionalStepEvaluationP95RemainsWithinBudgetUnderNormalLoadProfile() {
    const int concurrentExecutions = 10;
    const int commandStepsPerRun = 50;
    const int conditionalStepsPerRun = 10;
    const double budgetMs = 200d;

    var sequence = BuildConditionalPerformanceSequence(commandStepsPerRun, conditionalStepsPerRun);
    var runner = new SequenceRunner(new StubRepo(sequence));

    var perConditionalStepDurations = new List<double>(concurrentExecutions);
    var tasks = Enumerable.Range(0, concurrentExecutions)
      .Select(async i => {
        var sw = Stopwatch.StartNew();
        var result = await runner.ExecuteAsync(
          sequence.Id,
          _ => Task.CompletedTask,
          conditionEvaluator: (_, _) => Task.FromResult(true),
          ct: CancellationToken.None).ConfigureAwait(false);
        sw.Stop();

        result.Status.Should().Be("Succeeded");
        result.ConditionTraces.Should().HaveCount(conditionalStepsPerRun);
        lock (perConditionalStepDurations) {
          perConditionalStepDurations.Add(sw.Elapsed.TotalMilliseconds / conditionalStepsPerRun);
        }
      });

    await Task.WhenAll(tasks).ConfigureAwait(false);

    var p95 = CalculateP95(perConditionalStepDurations);
    _output.WriteLine($"Conditional step evaluation p95: {p95:F2} ms (budget <= {budgetMs:F0} ms)");
    p95.Should().BeLessThanOrEqualTo(budgetMs);
  }

  private static CommandSequence BuildConditionalPerformanceSequence(int commandSteps, int conditionalSteps) {
    var sequence = new CommandSequence {
      Id = "perf-conditional-sequence",
      Name = "Performance Conditional Sequence"
    };

    var flowSteps = new List<FlowStep>();
    var flowLinks = new List<BranchLink>();

    for (var i = 0; i < commandSteps; i++) {
      flowSteps.Add(new FlowStep {
        StepId = $"cmd-{i}",
        Label = $"Command {i}",
        StepType = FlowStepType.Command,
        PayloadRef = $"cmd-{i}"
      });
    }

    for (var i = 0; i < conditionalSteps; i++) {
      var condition = new ConditionExpression {
        NodeType = ConditionNodeType.Operand,
        Operand = new ConditionOperand {
          OperandType = ConditionOperandType.ImageDetection,
          TargetRef = $"img-{i}",
          ExpectedState = "present",
          Threshold = 0.8
        }
      };

      flowSteps.Add(new FlowStep {
        StepId = $"cond-{i}",
        Label = $"Condition {i}",
        StepType = FlowStepType.Condition,
        Condition = condition
      });
    }

    flowSteps.Add(new FlowStep {
      StepId = "terminal",
      Label = "Terminal",
      StepType = FlowStepType.Terminal
    });

    // Interleave command and condition nodes: cmd-0 -> cond-0 -> cmd-1 -> ...
    for (var i = 0; i < conditionalSteps; i++) {
      flowLinks.Add(new BranchLink {
        LinkId = $"next-cmd-cond-{i}",
        SourceStepId = $"cmd-{i}",
        TargetStepId = $"cond-{i}",
        BranchType = BranchType.Next
      });
      flowLinks.Add(new BranchLink {
        LinkId = $"true-cond-cmd-{i}",
        SourceStepId = $"cond-{i}",
        TargetStepId = $"cmd-{i + 1}",
        BranchType = BranchType.True
      });
      flowLinks.Add(new BranchLink {
        LinkId = $"false-cond-cmd-{i}",
        SourceStepId = $"cond-{i}",
        TargetStepId = $"cmd-{i + 1}",
        BranchType = BranchType.False
      });
    }

    for (var i = conditionalSteps + 1; i < commandSteps; i++) {
      flowLinks.Add(new BranchLink {
        LinkId = $"next-cmd-{i}",
        SourceStepId = $"cmd-{i - 1}",
        TargetStepId = $"cmd-{i}",
        BranchType = BranchType.Next
      });
    }

    flowLinks.Add(new BranchLink {
      LinkId = "next-last-terminal",
      SourceStepId = $"cmd-{commandSteps - 1}",
      TargetStepId = "terminal",
      BranchType = BranchType.Next
    });

    sequence.EntryStepId = "cmd-0";
    sequence.SetFlowSteps(flowSteps);
    sequence.SetFlowLinks(flowLinks);
    return sequence;
  }

  private static double CalculateP95(List<double> values) {
    values.Sort();
    var index = (int)Math.Ceiling(values.Count * 0.95d) - 1;
    return values[Math.Max(0, index)];
  }

  private sealed class StubRepo : ISequenceRepository {
    private readonly CommandSequence _sequence;

    public StubRepo(CommandSequence sequence) {
      _sequence = sequence;
    }

    public Task<CommandSequence?> GetAsync(string id) => Task.FromResult<CommandSequence?>(_sequence);

    public Task<IReadOnlyList<CommandSequence>> ListAsync() =>
      Task.FromResult<IReadOnlyList<CommandSequence>>(new List<CommandSequence> { _sequence }.AsReadOnly());

    public Task<CommandSequence> CreateAsync(CommandSequence sequence) => Task.FromResult(sequence);

    public Task<CommandSequence> UpdateAsync(CommandSequence sequence) => Task.FromResult(sequence);

    public Task<bool> DeleteAsync(string id) => Task.FromResult(true);
  }
}
