using System.Linq;
using FluentAssertions;
using GameBot.Domain.Commands;
using GameBot.Domain.Gamepacks;
using Xunit;
using F = GameBot.UnitTests.Gamepacks.GamepackStepFixtures;

#pragma warning disable CA1707, CA1861, CA1305, CA1307, CA1310

namespace GameBot.UnitTests.Gamepacks;

/// <summary>Feature 151: the reader converts the API form of an Action step to the stored form.</summary>
public sealed class GamepackStepReaderTests {
  private static GamepackParseResult Read(params string[] steps) => GamepackFileSerializer.Parse(F.PackWith(steps));

  private static SequenceStep OnlyStep(GamepackParseResult result) => result.File!.Sequences.Single().Steps.Single();

  private static void ExpectCommandAction(SequenceStep step, string commandId = F.CommandId) {
    step.Action.Should().NotBeNull();
    step.Action!.Type.Should().Be("command");
    step.Action!.Parameters["commandId"]!.ToString().Should().Be(commandId);
    step.CommandId.Should().Be(commandId);
    step.CommandReference.Should().NotBeNull();
    step.CommandReference!.CommandId.Should().Be(commandId);
    step.StepType.Should().Be(SequenceStepType.Action);
  }

  // ---- forms (FR-002) ----

  [Fact]
  public void PrimitiveActionOnlyGivesTheActionAndTheReference() {
    var result = Read(F.PrimitiveOnly());
    result.Problems.Should().BeEmpty();
    ExpectCommandAction(OnlyStep(result));
  }

  [Fact]
  public void CommandReferenceOnlyGivesTheActionAndKeepsTheName() {
    var result = Read(F.ReferenceOnly());
    result.Problems.Should().BeEmpty();
    var step = OnlyStep(result);
    ExpectCommandAction(step);
    step.CommandReference!.CommandName.Should().Be(F.CommandName);
  }

  [Fact]
  public void CommandIdOnlyGivesTheActionAndTheReference() {
    var result = Read(F.CommandIdOnly());
    result.Problems.Should().BeEmpty();
    ExpectCommandAction(OnlyStep(result));
  }

  [Fact]
  public void AllThreeFormsGiveOneConsistentAction() {
    var result = Read(F.All());
    result.Problems.Should().BeEmpty();
    var step = OnlyStep(result);
    ExpectCommandAction(step);
    step.CommandReference!.CommandName.Should().Be(F.CommandName);
  }

  [Fact]
  public void CommandIdWithPrimitiveActionGivesAReference() {
    var result = Read(F.CommandIdAndPrimitive());
    result.Problems.Should().BeEmpty();
    ExpectCommandAction(OnlyStep(result));
  }

  [Fact]
  public void TheStoredFormStaysAsGiven() {
    var result = Read(F.Stored());
    result.Problems.Should().BeEmpty();
    var step = OnlyStep(result);
    ExpectCommandAction(step);
    step.CommandReference!.CommandName.Should().Be(F.CommandName);
  }

  [Fact]
  public void IsResolvedIsAcceptedAndNotStored() {
    var result = Read(F.ReferenceOnly());
    result.Problems.Should().BeEmpty();
    System.Text.Json.JsonSerializer.Serialize(OnlyStep(result).CommandReference).Should().NotContainEquivalentOf("isResolved");
  }

  [Fact]
  public void NamesOfStepMembersMatchWithoutCase() {
    var result = Read("{\"StepId\":\"s1\",\"StepType\":\"action\",\"PrimitiveAction\":{\"Type\":\"command\",\"Payload\":{\"CommandId\":\"cmd-1\"}}}");
    result.Problems.Should().BeEmpty();
    ExpectCommandAction(OnlyStep(result));
  }

  [Fact]
  public void AStepWithoutStepTypeCountsAsAnActionStep() {
    var result = Read("{\"stepId\":\"s1\",\"primitiveAction\":{\"type\":\"command\",\"payload\":{\"commandId\":\"cmd-1\"}}}");
    result.Problems.Should().BeEmpty();
    ExpectCommandAction(OnlyStep(result));
  }

  // ---- nesting (FR-003) ----

  [Fact]
  public void AnActionStepInALoopBodyKeepsItsAction() {
    var result = Read(F.InLoop(F.All("inner")));
    result.Problems.Should().BeEmpty();
    ExpectCommandAction(OnlyStep(result).Body.Single());
  }

  [Fact]
  public void AnActionStepInAConditionalBodyKeepsItsAction() {
    var result = Read(F.InConditionalBody(F.PrimitiveOnly("inner")));
    result.Problems.Should().BeEmpty();
    ExpectCommandAction(OnlyStep(result).Body.Single());
  }

  [Fact]
  public void AnActionStepInAnElseBodyKeepsItsAction() {
    var result = Read(F.InConditionalElseBody(F.ReferenceOnly("inner")));
    result.Problems.Should().BeEmpty();
    ExpectCommandAction(OnlyStep(result).ElseBody!.Single());
  }

  // ---- conflicts (R5) ----

  [Theory]
  [InlineData("ReferenceVersusPrimitive")]
  [InlineData("CommandIdVersusPrimitive")]
  [InlineData("ActionVersusPrimitiveCommand")]
  [InlineData("ActionVersusPrimitiveType")]
  public void AConflictGivesAStructureProblemWithTheSequenceAndTheStep(string form) {
    var step = typeof(F).GetMethod(form)!.Invoke(null, null)!.ToString()!;

    var result = Read(step);

    var problem = result.Problems.Should().ContainSingle().Subject;
    problem.Category.Should().Be("structure");
    problem.ObjectType.Should().Be("sequence");
    problem.Name.Should().Be(F.SequenceName);
    problem.Message.Should().Contain("'s1'");
  }

  [Fact]
  public void ActionWinsWhenPrimitiveActionAgrees() {
    var result = Read(F.ActionAgreesWithPrimitive());
    result.Problems.Should().BeEmpty();
    var step = OnlyStep(result);
    step.Action!.Parameters.Should().ContainKey("extra");
  }

  // ---- aliases on other step types (R11) ----

  [Theory]
  [InlineData(F.PrimitiveAlias, "primitiveAction")]
  [InlineData(F.ReferenceAlias, "commandReference")]
  [InlineData(F.CommandIdAlias, "commandId")]
  public void AnAliasOnALoopStepGivesAProblem(string alias, string field) {
    var result = Read(F.LoopWith(alias));
    result.Problems.Should().ContainSingle(p => p.Category == "structure" && p.Message.Contains("'" + field + "'") && p.Message.Contains("'L1'"));
  }

  [Theory]
  [InlineData(F.PrimitiveAlias, "primitiveAction")]
  [InlineData(F.ReferenceAlias, "commandReference")]
  public void AnAliasOnAConditionalStepGivesAProblem(string alias, string field) {
    var result = Read(F.ConditionalWith(alias));
    result.Problems.Should().ContainSingle(p => p.Message.Contains("'" + field + "'") && p.Message.Contains("'C1'"));
  }

  [Theory]
  [InlineData(F.PrimitiveAlias, "primitiveAction")]
  [InlineData(F.ReferenceAlias, "commandReference")]
  public void AnAliasOnACommandStepGivesAProblem(string alias, string field) {
    var result = Read(F.CommandStepWith(alias));
    result.Problems.Should().ContainSingle(p => p.Message.Contains("'" + field + "'") && p.Message.Contains("'M1'"));
  }

  [Fact]
  public void AStoredCommandStepWithCommandIdReadsAsBefore() {
    var result = Read("{\"stepId\":\"m1\",\"stepType\":\"Command\",\"commandId\":\"cmd-1\"}");
    result.Problems.Should().BeEmpty();
    var step = OnlyStep(result);
    step.StepType.Should().Be(SequenceStepType.Command);
    step.CommandId.Should().Be("cmd-1");
    step.Action.Should().BeNull();
  }

  [Fact]
  public void ExportedNullAndEmptyAliasesOnALoopStepAreNotProblems() {
    var result = Read("{\"stepId\":\"L1\",\"stepType\":\"Loop\",\"CommandId\":\"\",\"CommandReference\":null,\"Action\":null,\"loop\":{\"loopType\":\"count\",\"count\":1},\"body\":[]}");
    result.Problems.Should().BeEmpty();
  }

  // ---- unknown members (FR-004) ----

  [Fact]
  public void AnUnknownMemberOnAnActionStepGivesAProblem() {
    var result = Read(F.UnknownMember());
    var problem = result.Problems.Should().ContainSingle().Subject;
    problem.Category.Should().Be("structure");
    problem.Name.Should().Be(F.SequenceName);
    problem.Message.Should().Contain("'mystery'").And.Contain("'s1'");
  }

  [Fact]
  public void AnUnknownMemberOnALoopStepGivesAProblem() {
    var result = Read("{\"stepId\":\"L1\",\"stepType\":\"Loop\",\"loop\":{\"loopType\":\"count\",\"count\":1},\"body\":[],\"bogus\":true}");
    result.Problems.Should().ContainSingle(p => p.Message.Contains("'bogus'") && p.Message.Contains("'L1'"));
  }

  [Fact]
  public void AnUnknownMemberOnANestedStepGivesAProblem() {
    var inner = "{\"stepId\":\"deep\",\"stepType\":\"Action\",\"commandId\":\"cmd-1\",\"bogus\":true}";
    Read(F.InLoop(inner)).Problems.Should().ContainSingle(p => p.Message.Contains("'bogus'") && p.Message.Contains("'deep'"));
    Read(F.InConditionalElseBody(inner)).Problems.Should().ContainSingle(p => p.Message.Contains("'deep'"));
  }

  [Fact]
  public void MembersInsideANestedPayloadAreNotChecked() {
    var result = Read("{\"stepId\":\"s1\",\"stepType\":\"Action\",\"primitiveAction\":{\"type\":\"command\",\"payload\":{\"commandId\":\"cmd-1\",\"anything\":1}},\"commandReference\":{\"commandId\":\"cmd-1\",\"isResolved\":false,\"extra\":2}}");
    result.Problems.Should().BeEmpty();
  }
}
