using System;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using GameBot.Domain.Commands;
using GameBot.Domain.Gamepacks;
using GameBot.UnitTests.Gamepacks;
using Xunit;
using Xunit.Abstractions;
using F = GameBot.UnitTests.Gamepacks.GamepackStepFixtures;

#pragma warning disable CA2007, CA1861, CA1707, CA1416, CA1305, CA1307, CA1310

namespace GameBot.IntegrationTests.Gamepacks;

/// <summary>Feature 151: export then install keeps the command action (FR-006, SC-004).</summary>
public sealed class GamepackActionStepRoundTripTests : IDisposable {
  private readonly GamepackTestKit _source = new();
  private readonly GamepackTestKit _target = new();
  private readonly ITestOutputHelper _output;

  public GamepackActionStepRoundTripTests(ITestOutputHelper output) {
    _output = output;
  }

  public void Dispose() {
    _source.Dispose();
    _target.Dispose();
  }

  private static async Task InstallAsync(GamepackTestKit kit, string json) {
    var outcome = await kit.Service.InstallAsync(json, dryRun: false);
    outcome.Status.Should().Be(GamepackInstallStatus.Ok, string.Join("; ", outcome.Problems.Select(p => p.Message)) + outcome.ErrorMessage);
  }

  // Builds the export of the stored objects of a kit, as the exporter does: the stored form of each object.
  private static async Task<string> ExportAsync(GamepackTestKit kit, string version) {
    var file = new GamepackFile {
      Manifest = new GamepackManifest { Name = F.PackName, Version = version },
      Sequences = await kit.Sequences.ListAsync(),
      Commands = await kit.Commands.ListAsync()
    };
    return GamepackFileSerializer.Serialize(file);
  }

  private static void ExpectCommandAction(SequenceStep step, string commandId, string? commandName) {
    step.Action.Should().NotBeNull();
    step.Action!.Type.Should().Be("command");
    step.Action!.Parameters["commandId"]!.ToString().Should().Be(commandId);
    step.CommandId.Should().Be(commandId);
    step.CommandReference!.CommandId.Should().Be(commandId);
    if (commandName is not null) {
      step.CommandReference.CommandName.Should().Be(commandName);
    }
  }

  [Fact]
  public async Task ExportThenInstallKeepsTheCommandAction() {
    await InstallAsync(_source, F.Issue321Pack());
    var exported = await ExportAsync(_source, "1.0.0");

    await InstallAsync(_target, exported);

    var sourceStep = (await _source.SequenceByNameAsync(F.SequenceName))!.Steps.Single();
    var targetStep = (await _target.SequenceByNameAsync(F.SequenceName))!.Steps.Single();
    ExpectCommandAction(targetStep, F.CommandId, F.CommandName);
    targetStep.Action!.Type.Should().Be(sourceStep.Action!.Type);
    targetStep.CommandReference!.CommandName.Should().Be(sourceStep.CommandReference!.CommandName);
    targetStep.StepType.Should().Be(sourceStep.StepType);
  }

  [Fact]
  public async Task ASecondInstallKeepsTheCommandAction() {
    await InstallAsync(_source, F.Issue321Pack());
    await InstallAsync(_target, await ExportAsync(_source, "1.0.0"));

    await InstallAsync(_target, await ExportAsync(_source, "1.0.1"));

    var step = (await _target.SequenceByNameAsync(F.SequenceName))!.Steps.Single();
    ExpectCommandAction(step, F.CommandId, F.CommandName);
  }

  [Fact]
  public async Task ANewCommandIdReachesEveryStepInEveryList() {
    // A user command that holds the id of the file command forces the install to give the file command a new id.
    await _target.AddUserObjectsAsync(GamepackTestKit.Pack("user", "1", commands: new[] { GamepackTestKit.Cmd(F.CommandId, "Other command") }));

    await InstallAsync(_target, F.PackWith(
      F.All("a"),
      F.InLoop(F.PrimitiveOnly("b")),
      F.InConditionalBody(F.ReferenceOnly("c")),
      F.InConditionalElseBody(F.CommandIdOnly("d"))));

    var installed = (await _target.CommandByNameAsync(F.CommandName))!;
    installed.Id.Should().NotBe(F.CommandId);
    var steps = (await _target.SequenceByNameAsync(F.SequenceName))!.Steps;
    ExpectCommandAction(steps[0], installed.Id, null);
    ExpectCommandAction(steps[1].Body.Single(), installed.Id, null);
    ExpectCommandAction(steps[2].Body.Single(), installed.Id, null);
    ExpectCommandAction(steps[3].ElseBody!.Single(), installed.Id, null);
  }

  [Fact]
  public void AnotherActionTypeIsMeasured() {
    var step = "{\"stepId\":\"w1\",\"stepType\":\"Action\",\"primitiveAction\":{\"type\":\"WaitForImage\",\"payload\":{\"imageId\":\"img-1\",\"timeoutMs\":500}}}";
    var parsed = GamepackFileSerializer.Parse(F.PackWith(step));

    var stored = parsed.File!.Sequences.Single().Steps.Single();

    // Measure only. A loss of other action types is out of scope for feature 151 (see research R9).
    _output.WriteLine($"Action.Type={stored.Action?.Type}; parameters={stored.Action?.Parameters.Count}; WaitForImage config set={stored.WaitForImage is not null}");
    parsed.Problems.Should().BeEmpty();
    stored.Action.Should().NotBeNull();
    stored.Action!.Type.Should().Be("WaitForImage");
  }
}
