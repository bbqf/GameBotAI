using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using GameBot.Domain.Actions;
using GameBot.Domain.Commands;
using GameBot.Domain.Parameters;
using GameBot.Domain.Services;
using Xunit;

#pragma warning disable CA2007, CA1861, CA1859

namespace GameBot.UnitTests.Sequences;

/// <summary>
/// Reproduction of a live failure: "PNS Pit Ensure Mining" logged
/// <c>Step 'Tap Enter Field row7' failed: tap step requires numeric 'x' and 'y' parameters</c>
/// while the sibling top-level tap (literal coordinates) succeeded in the same run.
/// <para>
/// The shape is exactly the stored one: a <b>top-level If</b> whose then-branch holds an inline tap
/// whose <c>y</c> is <c>{{sectionRowY}}</c>, with the parameter declared on the sequence with a
/// default and no caller supplying anything.
/// </para>
/// </summary>
public sealed class SequenceRunnerIfBodyScopeTests {
  private sealed class StubRepo : ISequenceRepository {
    private readonly CommandSequence _sequence;
    public StubRepo(CommandSequence sequence) => _sequence = sequence;
    public Task<CommandSequence?> GetAsync(string id) => Task.FromResult<CommandSequence?>(_sequence);
    public Task<IReadOnlyList<CommandSequence>> ListAsync() => Task.FromResult<IReadOnlyList<CommandSequence>>(new List<CommandSequence> { _sequence });
    public Task<CommandSequence> CreateAsync(CommandSequence sequence) => Task.FromResult(sequence);
    public Task<CommandSequence> UpdateAsync(CommandSequence sequence) => Task.FromResult(sequence);
    public Task<bool> DeleteAsync(string id) => Task.FromResult(true);
  }

  /// <summary>The stored Pit shape: If(imageVisible) → body[ tap(448, {{sectionRowY}}) ].</summary>
  private static CommandSequence PitSequence() {
    var sequence = new CommandSequence { Id = "pit", Name = "PNS Pit Ensure Mining" };
    sequence.Parameters.Add(new ParameterDeclaration {
      Name = "sectionRowY", Type = ParameterValueType.Number, Default = "569"
    });
    sequence.SetSteps(new[] {
      new SequenceStep {
        Order = 0,
        StepId = "enter7",
        StepType = SequenceStepType.If,
        If = new IfConfig {
          Condition = new ImageVisibleStepCondition { ImageId = "rare-earth-title", MinSimilarity = 0.8 }
        },
        Body = new[] {
          new SequenceStep {
            Order = 0,
            StepId = "enter7-tap",
            Label = "Tap Enter Field row7",
            StepType = SequenceStepType.Action,
            Action = new SequenceActionPayload {
              Type = ActionTypes.Tap,
              Parameters = { ["x"] = 448, ["y"] = "{{sectionRowY}}" }
            }
          }
        }
      }
    });
    return sequence;
  }

  private static async Task<SequenceActionPayload?> RunAsync(ParameterScope scope) {
    var sequence = PitSequence();
    var runner = new SequenceRunner(new StubRepo(sequence));
    SequenceActionPayload? captured = null;

    await runner.ExecuteAsync(
      sequence.Id,
      (_, _) => Task.CompletedTask,
      conditionEvaluator: (_, _) => Task.FromResult(true), // the image is visible, as in the live run
      actionDispatcher: (payload, _) => {
        captured = payload;
        return Task.FromResult(new ActionDispatchResult("executed", null));
      },
      scope: scope,
      ct: CancellationToken.None);

    return captured;
  }

  [Fact]
  public async Task ATapInsideAnIfBranchResolvesTheSequencesDeclaredDefault() {
    var payload = await RunAsync(ParameterScope.Empty);

    payload.Should().NotBeNull("the then-branch runs when the condition is true");
    payload!.Parameters["y"].Should().Be("569");
    payload.Parameters["x"].Should().Be(448);
  }

  /// <summary>
  /// The same sequence after a persistence round-trip, which is what actually runs.
  /// <para>
  /// <see cref="SequenceActionPayload.Parameters"/> is a <c>Dictionary&lt;string, object?&gt;</c>, so
  /// System.Text.Json materializes every value as a <see cref="JsonElement"/> — never a
  /// <see cref="string"/>. A test that builds the payload in memory therefore exercises a type the
  /// runner never sees at run time, and passes while production fails.
  /// </para>
  /// </summary>
  private static CommandSequence RoundTripped() {
    var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
    var json = JsonSerializer.Serialize(PitSequence(), options);
    return JsonSerializer.Deserialize<CommandSequence>(json, options)!;
  }

  [Fact]
  public async Task ATapLoadedFromPersistenceResolvesTheParameter() {
    var sequence = RoundTripped();
    sequence.Steps[0].Body![0].Action!.Parameters["y"]
        .Should().BeOfType<JsonElement>("persistence yields JsonElement, not string — this is the gap");

    var runner = new SequenceRunner(new StubRepo(sequence));
    SequenceActionPayload? captured = null;
    await runner.ExecuteAsync(
      sequence.Id,
      (_, _) => Task.CompletedTask,
      conditionEvaluator: (_, _) => Task.FromResult(true),
      actionDispatcher: (payload, _) => {
        captured = payload;
        return Task.FromResult(new ActionDispatchResult("executed", null));
      },
      scope: ParameterScope.Empty,
      ct: CancellationToken.None);

    captured.Should().NotBeNull();
    // Must be a value a numeric slot can parse — not the literal placeholder.
    captured!.Parameters["y"].Should().NotBeNull();
    captured.Parameters["y"]!.ToString().Should().Be("569");
  }

  [Fact]
  public async Task AnEntryValueOverridesTheDefaultForATapInsideAnIfBranch() {
    var scope = ParameterScope.Empty
        .Child(ParameterScopeLayers.Entry,
            new Collection<ParameterBinding> { new() { Name = "sectionRowY", Value = "631" } },
            null);

    var payload = await RunAsync(scope);

    payload!.Parameters["y"].Should().Be("631");
  }
}
