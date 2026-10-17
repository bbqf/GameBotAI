using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using GameBot.Domain.Commands;
using GameBot.Domain.Gamepacks;
using Xunit;

#pragma warning disable CA1707, CA2007, CA1861, CA1416

namespace GameBot.UnitTests.Gamepacks;

/// <summary>Feature 155: the dry run checks every command reference form of a sequence step.</summary>
public sealed class GamepackValidatorCommandReferenceTests : IDisposable {
  private readonly GamepackTestKit _kit = new();

  public void Dispose() => _kit.Dispose();

  private static object SeqWith(string id, string name, params object[] steps) => new { id, name, version = 1, steps };

  // A command reference or an action is only valid on an Action step. A plain command id fits a Command step.
  private static object Step(int order, object? commandReference = null, string? commandId = null, object? action = null, string? stepType = null) =>
    new Dictionary<string, object?> {
      ["order"] = order,
      ["stepId"] = "s" + order,
      ["stepType"] = stepType ?? (commandReference is null && action is null && commandId is not null ? "Command" : "Action"),
      ["commandId"] = commandId ?? string.Empty,
      ["commandReference"] = commandReference,
      ["action"] = action
    };

  private static object Ref(string? id, string? name = null) => new { commandId = id ?? string.Empty, commandName = name };

  private static object CmdAction(string commandId) =>
    new { type = "command", parameters = new { commandId } };

  private async Task<GamepackValidationResult> CheckAsync(object[] sequences, object[]? commands = null) =>
    await _kit.CheckAsync(GamepackTestKit.Pack("p", "1", sequences: sequences, commands: commands));

  private static void ShouldHaveOneProblem(GamepackValidationResult result, string value) {
    result.IsValid.Should().BeFalse();
    var problem = result.Problems.Should().ContainSingle().Subject;
    problem.Category.Should().Be("reference");
    problem.ObjectType.Should().Be("sequence");
    problem.Message.Should().Be($"Command '{value}' is not in the gamepack or in the installation.");
  }

  [Fact]
  public async Task UnresolvedStepCommandIdIsReported() {
    var result = await CheckAsync(new[] { SeqWith("s", "Seq", Step(0, commandId: "ghost")) });
    ShouldHaveOneProblem(result, "ghost");
  }

  [Fact]
  public async Task UnresolvedCommandReferenceIdIsReported() {
    var result = await CheckAsync(new[] { SeqWith("s", "Seq", Step(0, commandReference: Ref("ghost"))) });
    ShouldHaveOneProblem(result, "ghost");
  }

  [Fact]
  public async Task UnresolvedActionPayloadIdIsReported() {
    var result = await CheckAsync(new[] { SeqWith("s", "Seq", Step(0, action: CmdAction("ghost"), stepType: "Action")) });
    ShouldHaveOneProblem(result, "ghost");
  }

  [Fact]
  public async Task ActionPayloadIdAsStringValueIsReported() {
    var file = GamepackFileSerializer.Parse(GamepackTestKit.Pack("p", "1", sequences: new[] { SeqWith("s", "Seq") })).File!;
    var step = new SequenceStep { StepId = "a", StepType = SequenceStepType.Action, Action = new SequenceActionPayload { Type = "command" } };
    step.Action.Parameters["commandId"] = "ghost";
    file.Sequences[0].SetSteps(new[] { step });
    var result = await _kit.Validator.ValidateAsync(file, Array.Empty<GamepackProblem>());
    ShouldHaveOneProblem(result, "ghost");

    step.Action.Parameters["commandId"] = JsonDocument.Parse("\"ghost2\"").RootElement.Clone();
    result = await _kit.Validator.ValidateAsync(file, Array.Empty<GamepackProblem>());
    ShouldHaveOneProblem(result, "ghost2");
  }

  [Fact]
  public async Task OnlyTheUnknownOfThreeIdFormsIsReported() {
    var cmd = GamepackTestKit.Cmd("c1", "Known");
    var step = Step(0, commandReference: Ref("c1"), commandId: "c1", action: CmdAction("ghost"), stepType: "Action");
    var result = await CheckAsync(new[] { SeqWith("s", "Seq", step) }, new[] { cmd });
    ShouldHaveOneProblem(result, "ghost");
  }

  [Fact]
  public async Task UnresolvedNameOnlyReferenceIsReported() {
    var result = await CheckAsync(new[] { SeqWith("s", "Seq", Step(0, commandReference: Ref(null, "Nowhere"))) });
    ShouldHaveOneProblem(result, "Nowhere");
  }

  [Fact]
  public async Task NameMatchIsCaseInsensitiveAndTrimmed() {
    var cmd = GamepackTestKit.Cmd("c1", "Help All");
    var result = await CheckAsync(new[] { SeqWith("s", "Seq", Step(0, commandReference: Ref(null, "  help ALL "))) }, new[] { cmd });
    result.IsValid.Should().BeTrue();
  }

  [Fact]
  public async Task ResolvedByIdInGamepackGivesNoProblem() {
    var cmd = GamepackTestKit.Cmd("c1", "One");
    var result = await CheckAsync(new[] { SeqWith("s", "Seq", Step(0, commandReference: Ref("c1")), Step(1, action: CmdAction("c1"), stepType: "Action")) }, new[] { cmd });
    result.IsValid.Should().BeTrue();
  }

  [Fact]
  public async Task ResolvedOnTheInstanceGivesNoProblem() {
    await _kit.Commands.AddAsync(new Command { Id = "inst", Name = "Instance Cmd" });
    var result = await CheckAsync(new[] { SeqWith("s", "Seq", Step(0, commandReference: Ref("inst")), Step(1, commandReference: Ref(null, "instance cmd"))) });
    result.IsValid.Should().BeTrue();
  }

  [Fact]
  public async Task StepWithNoCommandReferenceGivesNoProblem() {
    var result = await CheckAsync(new[] { SeqWith("s", "Seq", Step(0)) });
    result.IsValid.Should().BeTrue();
  }

  [Fact]
  public async Task NestedStepInALoopBodyIsChecked() {
    var file = GamepackFileSerializer.Parse(GamepackTestKit.Pack("p", "1", sequences: new[] { SeqWith("s", "Seq") })).File!;
    var inner = new SequenceStep { StepId = "b", CommandReference = new SequenceCommandReference { CommandName = "Nowhere" } };
    var loop = new SequenceStep { StepId = "l", Body = new[] { inner } };
    file.Sequences[0].SetSteps(new[] { loop });
    var result = await _kit.Validator.ValidateAsync(file, Array.Empty<GamepackProblem>());
    ShouldHaveOneProblem(result, "Nowhere");
  }

  [Fact]
  public async Task NameNextToAnIdIsNotChecked() {
    var result = await CheckAsync(new[] { SeqWith("s", "Seq", Step(0, commandReference: Ref("c1", "Unknown Name"))) }, new[] { GamepackTestKit.Cmd("c1", "Known") });
    result.IsValid.Should().BeTrue();
  }

  [Fact]
  public async Task SameNameDoesNotResolveAMissingId() {
    var result = await CheckAsync(new[] { SeqWith("s", "Seq", Step(0, commandReference: Ref("ghost", "Known"))) }, new[] { GamepackTestKit.Cmd("c1", "Known") });
    ShouldHaveOneProblem(result, "ghost");
  }

  [Fact]
  public async Task DuplicateMissingValueGivesOneProblemPerSequence() {
    var result = await CheckAsync(new[] { SeqWith("s", "Seq", Step(0, commandId: "ghost"), Step(1, commandReference: Ref("ghost"))) });
    ShouldHaveOneProblem(result, "ghost");
  }

  [Fact]
  public async Task RenameAfterConflictStillResolvesANameOnlyStep() {
    await _kit.Commands.AddAsync(new Command { Id = "shared-id", Name = "User Cmd" });
    var result = await CheckAsync(
      new[] { SeqWith("s", "Seq", Step(0, commandReference: Ref(null, "Pack Cmd"))) },
      new[] { GamepackTestKit.Cmd("shared-id", "Pack Cmd") });
    result.IsValid.Should().BeTrue();
    result.Plan!.Writes.Should().Contain(w => w.FinalId != "shared-id" && w.Type == "command");
  }

  [Fact]
  public async Task CommandToCommandReferenceIsUnchanged() {
    var good = await _kit.CheckAsync(GamepackTestKit.Pack("p", "1", commands: new[] {
      GamepackTestKit.Cmd("a", "A", callsCommandId: "b"), GamepackTestKit.Cmd("b", "B") }));
    good.IsValid.Should().BeTrue();
    var bad = await _kit.CheckAsync(GamepackTestKit.Pack("p", "1", commands: new[] { GamepackTestKit.Cmd("a", "A", callsCommandId: "ghost") }));
    var problem = bad.Problems.Should().ContainSingle().Subject;
    problem.Category.Should().Be("reference");
    problem.ObjectType.Should().Be("command");
    problem.Message.Should().Be("Command 'ghost' is not in the gamepack or in the installation.");
  }
}
