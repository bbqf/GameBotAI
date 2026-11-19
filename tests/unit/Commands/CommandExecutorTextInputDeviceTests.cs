using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Runtime.Versioning;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using GameBot.Domain.Commands;
using GameBot.Domain.Parameters;
using GameBot.Domain.Services;
using GameBot.Domain.Triggers;
using GameBot.Domain.Triggers.Evaluators;
using GameBot.Service.Services;
using GameBot.UnitTests.Emulator;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

// Test-code analyzer relaxations permitted by the constitution:
#pragma warning disable CA2007, CA1861, CA1859

namespace GameBot.UnitTests.Commands;

/// <summary>
/// Feature 195: the executor runs a TextInput step on a <see cref="GameBot.Emulator.Session.SessionManager"/>
/// with a fake device. The device gets one input command for each character, with the pause between
/// two characters, for a literal text and for a text from a placeholder.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class CommandExecutorTextInputDeviceTests {
  private sealed class FakeCommandRepository : ICommandRepository {
    private readonly Dictionary<string, Command> _cmds = new(StringComparer.Ordinal);
    public void Seed(Command c) => _cmds[c.Id] = c;
    public Task<Command> AddAsync(Command c, CancellationToken ct = default) { _cmds[c.Id] = c; return Task.FromResult(c); }
    public Task<Command?> GetAsync(string id, CancellationToken ct = default) => Task.FromResult(_cmds.TryGetValue(id, out var c) ? c : null);
    public Task<IReadOnlyList<Command>> ListAsync(CancellationToken ct = default) => Task.FromResult((IReadOnlyList<Command>)_cmds.Values.ToList());
    public Task<Command?> UpdateAsync(Command c, CancellationToken ct = default) { _cmds[c.Id] = c; return Task.FromResult<Command?>(c); }
    public Task<bool> DeleteAsync(string id, CancellationToken ct = default) => Task.FromResult(_cmds.Remove(id));
  }

  private sealed class FakeTriggerRepository : ITriggerRepository {
    public Task<Trigger?> GetAsync(string id, CancellationToken ct = default) => Task.FromResult<Trigger?>(null);
    public Task<IReadOnlyList<Trigger>> ListAsync(CancellationToken ct = default) => Task.FromResult((IReadOnlyList<Trigger>)Array.Empty<Trigger>());
    public Task UpsertAsync(Trigger t, CancellationToken ct = default) => Task.CompletedTask;
    public Task<bool> DeleteAsync(string id, CancellationToken ct = default) => Task.FromResult(false);
  }

  private sealed class FakeSessionContextCache : ISessionContextCache {
    public void SetSessionId(string g, string a, string s) { }
    public string? GetSessionId(string g, string a) => null;
    public void ClearSession(string g, string a) { }
  }

  private static readonly string[] ThreeCharactersWithPauses = { "text:2", "pause:100", "text:5", "pause:100", "text:0" };

  private static Command TextCommand(CommandStep step, bool withParameter) {
    var command = new Command {
      Id = "cmd1",
      Name = "TextInput",
      TriggerId = null,
      Steps = new Collection<CommandStep> { step }
    };
    if (withParameter) {
      command.Parameters.Add(new ParameterDeclaration { Name = "fighterBatch", Type = ParameterValueType.Number, Default = "250" });
    }
    return command;
  }

  private static async Task<(CommandForceExecutionResult Result, FakeAdbSessionClient Device)> RunAsync(
      CommandStep step, bool withParameter = false, string? failCharacter = null) {
    var cmds = new FakeCommandRepository();
    cmds.Seed(TextCommand(step, withParameter));
    var device = new FakeAdbSessionClient { FailTextChunk = failCharacter };
    var sessions = SeamSessionManager.Create(device, null, textInputCharDelayMs: 100);
    SeamSessionManager.RecordPauses(sessions, device);
    var sessionId = SeamSessionManager.CreateDeviceSession(sessions);
    var executor = new CommandExecutor(cmds, sessions, new FakeTriggerRepository(),
        new TriggerEvaluationService(Array.Empty<ITriggerEvaluator>()),
        NullLogger<CommandExecutor>.Instance,
        new FakeSessionContextCache());
    var result = await executor.ForceExecuteDetailedAsync(sessionId, "cmd1");
    return (result, device);
  }

  private static CommandStep LiteralStep(string text) =>
    new() { Type = CommandStepType.TextInput, TextInput = new TextInputConfig { Text = text }, Order = 1 };

  [Fact]
  public async Task ALiteralTextGoesOneCharacterAtATimeAndIsExecuted() {
    var (result, device) = await RunAsync(LiteralStep("250"));

    device.Calls.Should().Equal(ThreeCharactersWithPauses);
    var outcome = result.StepOutcomes.Should().ContainSingle().Subject;
    outcome.Status.Should().Be("executed");
    outcome.TypedLength.Should().Be(3);
  }

  [Fact]
  public async Task ATextFromTheFieldTemplatesKeyGoesOneCharacterAtATime() {
    var step = new CommandStep {
      Type = CommandStepType.TextInput,
      TextInput = new TextInputConfig { Text = string.Empty },
      FieldTemplates = new Dictionary<string, string> { ["textInput.text"] = "{{fighterBatch}}" },
      Order = 1
    };

    var (result, device) = await RunAsync(step, withParameter: true);

    device.Calls.Should().Equal(ThreeCharactersWithPauses);
    var outcome = result.StepOutcomes.Should().ContainSingle().Subject;
    outcome.Status.Should().Be("executed");
    outcome.TypedLength.Should().Be(3);
  }

  [Fact]
  public async Task AnInlinePlaceholderTextGoesOneCharacterAtATime() {
    var (result, device) = await RunAsync(LiteralStep("{{fighterBatch}}"), withParameter: true);

    device.Calls.Should().Equal(ThreeCharactersWithPauses);
    var outcome = result.StepOutcomes.Should().ContainSingle().Subject;
    outcome.Status.Should().Be("executed");
    outcome.TypedLength.Should().Be(3);
  }

  [Fact]
  public async Task AFailedCharacterGivesNotAcceptedWithTheAcceptedCount() {
    var (result, _) = await RunAsync(LiteralStep("250"), failCharacter: "0");

    result.Accepted.Should().Be(0);
    var outcome = result.StepOutcomes.Should().ContainSingle().Subject;
    outcome.Status.Should().Be("not_accepted");
    outcome.Reason.Should().Be("text_input_not_accepted");
    outcome.TypedLength.Should().Be(2);
  }
}
