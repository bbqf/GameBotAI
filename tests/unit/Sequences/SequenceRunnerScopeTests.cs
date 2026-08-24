using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
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
/// Feature 078: parameter substitution inside a sequence's own <b>inline action payloads</b> — the
/// shape that carries tap coordinates.
/// <para>
/// This is the real-world case behind the Pit "which section to enter" question: the section is not a
/// numeric field anywhere, it is a fixed tap coordinate in an inline action step. A numeric payload
/// slot accepts a <c>{{name}}</c> string because every consumer parses defensively, so no schema
/// change is needed — these tests pin that down so it cannot regress.
/// </para>
/// </summary>
public sealed class SequenceRunnerScopeTests {
  private sealed class StubRepo : ISequenceRepository {
    private readonly CommandSequence _sequence;
    public StubRepo(CommandSequence sequence) => _sequence = sequence;
    public Task<CommandSequence?> GetAsync(string id) => Task.FromResult<CommandSequence?>(_sequence);
    public Task<IReadOnlyList<CommandSequence>> ListAsync() => Task.FromResult<IReadOnlyList<CommandSequence>>(new List<CommandSequence> { _sequence });
    public Task<CommandSequence> CreateAsync(CommandSequence sequence) => Task.FromResult(sequence);
    public Task<CommandSequence> UpdateAsync(CommandSequence sequence) => Task.FromResult(sequence);
    public Task<bool> DeleteAsync(string id) => Task.FromResult(true);
  }

  /// <summary>A sequence with one inline tap whose Y comes from <paramref name="yValue"/>.</summary>
  private static CommandSequence TapSequence(object yValue, params ParameterDeclaration[] declarations) {
    var sequence = new CommandSequence { Id = "pit", Name = "Pit Ensure Mining" };
    foreach (var declaration in declarations) sequence.Parameters.Add(declaration);
    sequence.SetSteps(new[] {
      new SequenceStep {
        Order = 0,
        StepId = "enter-row",
        StepType = SequenceStepType.Action,
        Action = new SequenceActionPayload {
          Type = ActionTypes.Tap,
          Parameters = { ["x"] = 448, ["y"] = yValue }
        }
      }
    });
    return sequence;
  }

  private static async Task<SequenceActionPayload?> RunCapturingPayloadAsync(
      CommandSequence sequence, ParameterScope scope) {
    var runner = new SequenceRunner(new StubRepo(sequence));
    SequenceActionPayload? captured = null;

    await runner.ExecuteAsync(
      sequence.Id,
      (_, _) => Task.CompletedTask,
      actionDispatcher: (payload, _) => {
        captured = payload;
        return Task.FromResult(new ActionDispatchResult("executed", null));
      },
      scope: scope,
      ct: CancellationToken.None);

    return captured;
  }

  [Fact]
  public async Task InlineTapCoordinateResolvesFromASequenceParameter() {
    // The Pit case: the row to tap is supplied per queue-template entry rather than hard-coded.
    var sequence = TapSequence("{{sectionRowY}}", new ParameterDeclaration {
      Name = "sectionRowY", Type = ParameterValueType.Number
    });
    var scope = ParameterScope.Empty
        .Child(ParameterScopeLayers.Entry,
            new Collection<ParameterBinding> { new() { Name = "sectionRowY", Value = "569" } },
            null);

    var payload = await RunCapturingPayloadAsync(sequence, scope);

    payload.Should().NotBeNull();
    payload!.Parameters["y"].Should().Be("569");
    payload.Parameters["x"].Should().Be(448, "an unparametrized slot keeps its stored numeric value");
  }

  [Fact]
  public async Task InlineTapCoordinateFallsBackToTheDeclaredDefault() {
    var sequence = TapSequence("{{sectionRowY}}", new ParameterDeclaration {
      Name = "sectionRowY", Type = ParameterValueType.Number, Default = "569"
    });

    var payload = await RunCapturingPayloadAsync(sequence, ParameterScope.Empty);

    payload!.Parameters["y"].Should().Be("569");
  }

  [Fact]
  public async Task EntryValueOverridesTheDeclaredDefaultForAnInlineTap() {
    var sequence = TapSequence("{{sectionRowY}}", new ParameterDeclaration {
      Name = "sectionRowY", Type = ParameterValueType.Number, Default = "569"
    });
    var scope = ParameterScope.Empty
        .Child(ParameterScopeLayers.Entry,
            new Collection<ParameterBinding> { new() { Name = "sectionRowY", Value = "631" } },
            null);

    var payload = await RunCapturingPayloadAsync(sequence, scope);

    payload!.Parameters["y"].Should().Be("631");
  }

  [Fact]
  public async Task AQueueBuiltInResolvesInsideAnInlineActionPayload() {
    var sequence = new CommandSequence { Id = "s", Name = "S" };
    sequence.SetSteps(new[] {
      new SequenceStep {
        Order = 0,
        StepId = "connect",
        StepType = SequenceStepType.Action,
        Action = new SequenceActionPayload {
          Type = ActionTypes.EnsureEmulatorRunning,
          Parameters = { ["adbSerial"] = "{{queue.emulatorSerial}}" }
        }
      }
    });
    var scope = ParameterScope.FromQueue(new GameBot.Domain.Queues.ExecutionQueue {
      Id = "q", Name = "Q", EmulatorSerial = "emulator-5560"
    });

    var payload = await RunCapturingPayloadAsync(sequence, scope);

    payload!.Parameters["adbSerial"].Should().Be("emulator-5560");
  }

  [Fact]
  public async Task AnUnparametrizedPayloadIsDispatchedUnchanged() {
    // FR-032: the overwhelmingly common case must behave exactly as before the feature.
    var sequence = TapSequence(569);

    var payload = await RunCapturingPayloadAsync(sequence, ParameterScope.Empty);

    payload!.Parameters["y"].Should().Be(569);
    payload.Parameters["x"].Should().Be(448);
  }

  [Fact]
  public async Task TopLevelStepsAreSubstituted() {
    // Substitution used to run only inside loop bodies; a top-level step like this one is the
    // ordinary case for a parametrized sequence.
    var sequence = TapSequence("{{sectionRowY}}", new ParameterDeclaration { Name = "sectionRowY" });
    var scope = ParameterScope.Empty
        .Child(ParameterScopeLayers.Entry,
            new Collection<ParameterBinding> { new() { Name = "sectionRowY", Value = "700" } },
            null);

    var payload = await RunCapturingPayloadAsync(sequence, scope);

    payload!.Parameters["y"].Should().Be("700");
  }
}
