using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using GameBot.Domain.Commands;
using GameBot.Domain.Services;
using Xunit;

namespace GameBot.IntegrationTests.Sequences;

public sealed class ConditionalExecutionIntegrationTests {
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

  [Fact]
  public async Task ExecuteAsyncRoutesIfElseToThenBranchWhenConditionIsTrue() {
    var sequence = new CommandSequence { Id = "cond-int-true", Name = "conditional-true" };
    using var blockJson = JsonDocument.Parse("""
      {
        "type": "ifElse",
        "condition": { "source": "trigger", "targetId": "branch-flag", "mode": "Present" },
        "steps": [ { "order": 1, "commandId": "then-command" } ],
        "elseSteps": [ { "order": 1, "commandId": "else-command" } ]
      }
      """);
    sequence.SetBlocks(new object[] { blockJson.RootElement.Clone() });

    var executed = new List<string>();
    var runner = new SequenceRunner(new StubRepo(sequence));

    var result = await runner.ExecuteAsync(
      sequence.Id,
      commandId => {
        executed.Add(commandId);
        return Task.CompletedTask;
      },
      conditionEvaluator: (_, _) => Task.FromResult(true),
      ct: CancellationToken.None);

    result.Status.Should().Be("Succeeded");
    executed.Should().ContainSingle().Which.Should().Be("then-command");
  }

  [Fact]
  public async Task ExecuteAsyncRoutesIfElseToElseBranchWhenConditionIsFalse() {
    var sequence = new CommandSequence { Id = "cond-int-false", Name = "conditional-false" };
    using var blockJson = JsonDocument.Parse("""
      {
        "type": "ifElse",
        "condition": { "source": "trigger", "targetId": "branch-flag", "mode": "Present" },
        "steps": [ { "order": 1, "commandId": "then-command" } ],
        "elseSteps": [ { "order": 1, "commandId": "else-command" } ]
      }
      """);
    sequence.SetBlocks(new object[] { blockJson.RootElement.Clone() });

    var executed = new List<string>();
    var runner = new SequenceRunner(new StubRepo(sequence));

    var result = await runner.ExecuteAsync(
      sequence.Id,
      commandId => {
        executed.Add(commandId);
        return Task.CompletedTask;
      },
      conditionEvaluator: (_, _) => Task.FromResult(false),
      ct: CancellationToken.None);

    result.Status.Should().Be("Succeeded");
    executed.Should().ContainSingle().Which.Should().Be("else-command");
  }

  [Fact]
  public async Task ExecuteAsyncFailsAndStopsWhenConditionCannotBeEvaluated() {
    var sequence = new CommandSequence { Id = "cond-int-fail", Name = "conditional-fail" };
    using var blockJson = JsonDocument.Parse("""
      {
        "type": "ifElse",
        "condition": { "source": "trigger", "targetId": "branch-flag", "mode": "Present" },
        "steps": [ { "order": 1, "commandId": "then-command" } ],
        "elseSteps": [ { "order": 1, "commandId": "else-command" } ]
      }
      """);
    sequence.SetBlocks(new object[] { blockJson.RootElement.Clone() });

    var executed = new List<string>();
    var runner = new SequenceRunner(new StubRepo(sequence));

    var result = await runner.ExecuteAsync(
      sequence.Id,
      commandId => {
        executed.Add(commandId);
        return Task.CompletedTask;
      },
      conditionEvaluator: (_, _) => throw new InvalidOperationException("evaluator unavailable"),
      ct: CancellationToken.None);

    result.Status.Should().Be("Failed");
    executed.Should().BeEmpty();
  }
}