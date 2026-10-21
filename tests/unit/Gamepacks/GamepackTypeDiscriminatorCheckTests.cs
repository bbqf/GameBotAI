using System.Text.Json;
using FluentAssertions;
using GameBot.Domain.Commands;
using GameBot.Domain.Gamepacks;
using Xunit;

#pragma warning disable CA1707, CA2007, CA1861

namespace GameBot.UnitTests.Gamepacks;

/// <summary>Feature 154: the check of the type member before the typed read.</summary>
public sealed class GamepackTypeDiscriminatorCheckTests {
  private static readonly JsonSerializerOptions Options = GamepackFileSerializer.SequenceJsonOptions;

  private static GamepackTypeProblem? Check(string stepJson) {
    using var document = JsonDocument.Parse("{\"name\":\"S\",\"steps\":[" + stepJson + "]}");
    return GamepackTypeDiscriminatorCheck.Find(document.RootElement, typeof(CommandSequence), Options, "$.sequences[0]");
  }

  private static string Step(string breakCondition) => "{\"stepId\":\"a\",\"stepType\":\"Loop\",\"breakCondition\":" + breakCondition + "}";

  [Theory]
  [InlineData("{\"stepRef\":\"s\"}", "Missing")]
  [InlineData("{\"type\":\"nope\"}", "Unknown")]
  [InlineData("{\"type\":5}", "NotString")]
  [InlineData("{\"type\":null}", "NotString")]
  [InlineData("{\"type\":\"all\",\"type\":\"any\"}", "Duplicate")]
  public void AProblemGivesThePathAndTheReason(string condition, string reasonName) {
    var problem = Check(Step(condition));

    problem.Should().NotBeNull();
    problem!.Reason.ToString().Should().Be(reasonName);
    problem.Path.Should().Be("$.sequences[0].steps[0].breakCondition");
  }

  [Fact]
  public void AValidNodeGivesNoProblem() {
    Check(Step("{\"stepRef\":\"s\",\"type\":\"commandOutcome\"}")).Should().BeNull();
  }

  [Fact]
  public void AnOcrTextConditionPassesTheCheckAndLoads() {
    var ocrText = "{\"type\":\"ocrText\",\"region\":{\"x\":1,\"y\":2,\"width\":30,\"height\":40},\"expected\":\"0\"}";

    Check(Step(ocrText)).Should().BeNull();

    var json = "{\"name\":\"S\",\"steps\":[" + Step(ocrText) + "]}";
    var sequence = JsonSerializer.Deserialize<CommandSequence>(json, Options);
    var condition = sequence!.Steps[0].BreakCondition.Should().BeOfType<OcrTextStepCondition>().Subject;
    condition.Expected.Should().Be("0");
    condition.Region!.Width.Should().Be(30);
  }

  [Fact]
  public void ABadLoopConfigGivesThePathOfTheLoop() {
    var problem = Check("{\"stepId\":\"a\",\"stepType\":\"Loop\",\"loop\":{\"loopType\":\"nope\"}}");

    problem!.Path.Should().Be("$.sequences[0].steps[0].loop");
    problem.Reason.Should().Be(GamepackTypeProblemReason.Unknown);
  }

  [Fact]
  public void ANestedBadTypeGivesTheNestedPath() {
    var all = "{\"type\":\"all\",\"children\":[{\"type\":\"imageVisible\",\"imageId\":\"i\"},{\"imageId\":\"i\",\"negate\":false,\"type\":\"nope\"}]}";

    var problem = Check(Step(all));

    problem!.Reason.Should().Be(GamepackTypeProblemReason.Unknown);
    problem.Path.Should().Be("$.sequences[0].steps[0].breakCondition.children[1]");
  }
}
