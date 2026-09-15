using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using GameBot.Domain.Commands;
using GameBot.Domain.Services;
using Xunit;

namespace GameBot.IntegrationTests.Sequences;

public sealed class PerStepConditionExecutionPermutationIntegrationTests {
  [Theory]
  [InlineData(true, true, 2)]
  [InlineData(true, false, 1)]
  [InlineData(false, true, 1)]
  [InlineData(false, false, 0)]
  public async Task ExecutesExpectedCountAcrossImageVisibilityPermutations(bool mapVisible, bool bagVisible, int expectedExecuted) {
    var sequence = BuildImageSequence();
    var executed = new List<string>();
    var runner = new SequenceRunner(new StubRepo(sequence));

    var result = await runner.ExecuteAsync(
      sequence.Id,
      (commandId, _) => {
        executed.Add(commandId);
        return Task.CompletedTask;
      },
      conditionEvaluator: (condition, _) => Task.FromResult(condition.TargetId == "map-image" ? mapVisible : bagVisible),
      ct: CancellationToken.None);

    result.Status.Should().Be("Succeeded");
    executed.Should().HaveCount(expectedExecuted);
  }

  private static CommandSequence BuildImageSequence() {
    var sequence = new CommandSequence {
      Id = "per-step-permutations",
      Name = "Per Step Permutations"
    };

    sequence.SetSteps(new[] {
      new SequenceStep {
        Order = 0,
        StepId = "map-step",
        CommandId = "cmd-map",
        Action = new SequenceActionPayload { Type = "command", Parameters = { ["commandId"] = "cmd-map" } },
        Condition = new ImageVisibleStepCondition { ImageId = "map-image", MinSimilarity = 0.85 }
      },
      new SequenceStep {
        Order = 1,
        StepId = "bag-step",
        CommandId = "cmd-bag",
        Action = new SequenceActionPayload { Type = "command", Parameters = { ["commandId"] = "cmd-bag" } },
        Condition = new ImageVisibleStepCondition { ImageId = "bag-image", MinSimilarity = 0.85 }
      }
    });

    return sequence;
  }

  /// <summary>
  /// The B-011 scenario across every screen permutation (feature 088, issue #191, SC-002).
  /// <para>
  /// The reference image for the shared <c>Confirm</c> button matches two unrelated dialogs at full
  /// confidence, so a guard built on it alone fires on both — and on the wrong one that means tapping
  /// a paid-resource prompt. The composite guard adds the look-alike's own title as a second signal.
  /// The row that matters is (true, true): the button is up, but so is the gas dialog, and the step
  /// must not run.
  /// </para>
  /// </summary>
  [Theory]
  [InlineData(true, true, false)]   // both up: the look-alike dialog — must NOT act
  [InlineData(true, false, true)]   // only the button: the intended dialog — must act
  [InlineData(false, true, false)]  // no button: nothing to dismiss
  [InlineData(false, false, false)] // neither: nothing to dismiss
  public async Task CompositeGuardActsOnlyOnTheIntendedDialog(bool confirmVisible, bool gasTitleVisible, bool shouldExecute) {
    var sequence = BuildCompositeGuardSequence();
    var executed = new List<string>();
    var runner = new SequenceRunner(new StubRepo(sequence));

    var result = await runner.ExecuteAsync(
      sequence.Id,
      (commandId, _) => {
        executed.Add(commandId);
        return Task.CompletedTask;
      },
      conditionEvaluator: (condition, _) => Task.FromResult(
        condition.TargetId == "pns-disconnect-confirm" ? confirmVisible : gasTitleVisible),
      ct: CancellationToken.None);

    result.Status.Should().Be("Succeeded");
    executed.Should().HaveCount(shouldExecute ? 1 : 0);
  }

  /// <summary>
  /// The same guard expressed with a nested <c>none</c> instead of <c>negate</c> must behave
  /// identically, since the quickstart offers both spellings as equivalent.
  /// </summary>
  [Theory]
  [InlineData(true, true, false)]
  [InlineData(true, false, true)]
  [InlineData(false, true, false)]
  [InlineData(false, false, false)]
  public async Task TheNestedNoneSpellingBehavesIdenticallyToTheNegateSpelling(bool confirmVisible, bool gasTitleVisible, bool shouldExecute) {
    var sequence = BuildCompositeGuardSequence(new AllStepCondition {
      Children = new SequenceStepCondition[] {
        new ImageVisibleStepCondition { ImageId = "pns-disconnect-confirm", MinSimilarity = 0.85 },
        new NoneStepCondition {
          Children = new SequenceStepCondition[] {
            new ImageVisibleStepCondition { ImageId = "pns-gas-dialog-title", MinSimilarity = 0.85 }
          }
        }
      }
    });

    var executed = new List<string>();
    var runner = new SequenceRunner(new StubRepo(sequence));

    await runner.ExecuteAsync(
      sequence.Id,
      (commandId, _) => {
        executed.Add(commandId);
        return Task.CompletedTask;
      },
      conditionEvaluator: (condition, _) => Task.FromResult(
        condition.TargetId == "pns-disconnect-confirm" ? confirmVisible : gasTitleVisible),
      ct: CancellationToken.None);

    executed.Should().HaveCount(shouldExecute ? 1 : 0);
  }

  private static CommandSequence BuildCompositeGuardSequence(SequenceStepCondition? guard = null) {
    var sequence = new CommandSequence {
      Id = "composite-guard-permutations",
      Name = "Composite Guard Permutations"
    };

    sequence.SetSteps(new[] {
      new SequenceStep {
        Order = 0,
        StepId = "dismiss-disconnect",
        CommandId = "cmd-dismiss-disconnect",
        Action = new SequenceActionPayload {
          Type = "command",
          Parameters = { ["commandId"] = "cmd-dismiss-disconnect" }
        },
        Condition = guard ?? new AllStepCondition {
          Children = new SequenceStepCondition[] {
            new ImageVisibleStepCondition { ImageId = "pns-disconnect-confirm", MinSimilarity = 0.85 },
            new ImageVisibleStepCondition { ImageId = "pns-gas-dialog-title", MinSimilarity = 0.85, Negate = true }
          }
        }
      }
    });

    return sequence;
  }

  private sealed class StubRepo : ISequenceRepository {
    private readonly CommandSequence _sequence;

    public StubRepo(CommandSequence sequence) {
      _sequence = sequence;
    }

    public Task<CommandSequence?> GetAsync(string id) => Task.FromResult<CommandSequence?>(_sequence);
    public Task<IReadOnlyList<CommandSequence>> ListAsync() => Task.FromResult<IReadOnlyList<CommandSequence>>(new List<CommandSequence> { _sequence });
    public Task<CommandSequence> CreateAsync(CommandSequence sequence) => Task.FromResult(sequence);
    public Task<CommandSequence> UpdateAsync(CommandSequence sequence) => Task.FromResult(sequence);
    public Task<bool> DeleteAsync(string id) => Task.FromResult(true);
  }
}
