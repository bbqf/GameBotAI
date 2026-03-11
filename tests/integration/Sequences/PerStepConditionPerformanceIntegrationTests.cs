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

public sealed class PerStepConditionPerformanceIntegrationTests {
  private readonly ITestOutputHelper _output;

  public PerStepConditionPerformanceIntegrationTests(ITestOutputHelper output) {
    _output = output;
  }

  [Fact]
  public async Task MixedPerStepConditionsP95RemainsWithinBudget() {
    const int totalSteps = 30;
    const int iterations = 60;
    const double budgetMs = 200d;

    var sequence = BuildMixedPerStepSequence(totalSteps);
    var runner = new SequenceRunner(new StubRepo(sequence));
    var durationsMs = new List<double>(iterations);

    for (var i = 0; i < iterations; i++) {
      var stopwatch = Stopwatch.StartNew();
      var result = await runner.ExecuteAsync(
        sequence.Id,
        _ => Task.CompletedTask,
        conditionEvaluator: (_, _) => Task.FromResult(true),
        ct: CancellationToken.None).ConfigureAwait(false);
      stopwatch.Stop();

      result.Status.Should().Be("Succeeded");
      durationsMs.Add(stopwatch.Elapsed.TotalMilliseconds);
    }

    var p95 = CalculateP95(durationsMs);
    _output.WriteLine($"Per-step mixed-condition p95: {p95:F2} ms (budget <= {budgetMs:F0} ms)");
    p95.Should().BeLessThanOrEqualTo(budgetMs);
  }

  private static CommandSequence BuildMixedPerStepSequence(int totalSteps) {
    var sequence = new CommandSequence {
      Id = "perf-per-step-mixed",
      Name = "Per-Step Mixed Performance"
    };

    var steps = new List<SequenceStep>(totalSteps);
    for (var i = 0; i < totalSteps; i++) {
      var stepId = $"step-{i + 1}";
      SequenceStepCondition? condition = null;

      if (i > 0 && i % 2 == 0) {
        condition = new ImageVisibleStepCondition {
          ImageId = $"image-{i}",
          MinSimilarity = 0.85
        };
      }
      else if (i > 0) {
        condition = new CommandOutcomeStepCondition {
          StepRef = $"step-{i}",
          ExpectedState = "success"
        };
      }

      steps.Add(new SequenceStep {
        Order = i,
        StepId = stepId,
        CommandId = $"cmd-{i + 1}",
        Action = new SequenceActionPayload {
          Type = "command",
          Parameters = { ["commandId"] = $"cmd-{i + 1}" }
        },
        Condition = condition
      });
    }

    sequence.SetSteps(steps);
    return sequence;
  }

  private static double CalculateP95(List<double> values) {
    values.Should().NotBeEmpty();
    var ordered = values.OrderBy(x => x).ToList();
    var index = (int)Math.Ceiling(ordered.Count * 0.95d) - 1;
    return ordered[Math.Max(0, index)];
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
