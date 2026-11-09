#pragma warning disable CA1861 // Test data arrays are small and used one time.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using GameBot.Domain.Commands;
using GameBot.Domain.Config;
using GameBot.Domain.Parameters;
using GameBot.Domain.Services;
using GameBot.Domain.Utils;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace GameBot.UnitTests.Sequences;

/// <summary>Feature 181: the matched image of an image check, the matchedImage condition, and the placeholder.</summary>
public sealed class MatchedImageTests {
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

  private static SequenceStep CommandStep(int order, string id, SequenceStepCondition? condition = null) => new() {
    Order = order,
    StepId = id,
    CommandId = "cmd-" + id,
    Condition = condition,
    Action = new SequenceActionPayload {
      Type = "command",
      Parameters = { ["commandId"] = "cmd-" + id }
    }
  };

  private static SequenceStep WaitStep(DetectionTarget target, string until = "present") => new() {
    Order = 0,
    StepId = "wait",
    CommandId = "wait",
    Action = new SequenceActionPayload { Type = "WaitForImage", Parameters = { ["timeoutMs"] = 80 } },
    WaitForImage = new WaitForImageConfig { TimeoutMs = 80, DetectionTarget = target, Until = until }
  };

  private static DetectionTarget List(params string[] ids) =>
    new(ids[0], 0.8, 0, 0, DetectionSelectionStrategy.HighestConfidence, null, ids);

  private static async Task<(SequenceExecutionResult Result, List<string> Executed)> RunAsync(
      IEnumerable<SequenceStep> steps, params string[] visible) {
    var sequence = new CommandSequence { Id = "seq", Name = "seq" };
    sequence.SetSteps(steps.ToList());
    var runner = new SequenceRunner(new StubRepo(sequence), NullLogger<SequenceRunner>.Instance, new AppConfig { CaptureIntervalMs = 10 });
    var executed = new List<string>();
    var result = await runner.ExecuteAsync(
      "seq",
      (commandId, _) => {
        executed.Add(commandId);
        return Task.CompletedTask;
      },
      conditionEvaluator: (c, _) => Task.FromResult(visible.Contains(c.TargetId)),
      ct: CancellationToken.None).ConfigureAwait(false);
    return (result, executed);
  }

  // ---------- Store ----------

  [Fact]
  public void StoreSetsReplacesAndClearsAndIgnoresKeyCase() {
    var scope = new StepImageMatchScope();
    scope.Set("Wait", "a", 0.9);
    scope.TryGet("wait", out var first).Should().BeTrue();
    first.MatchedImageId.Should().Be("a");
    first.Confidence.Should().Be(0.9);

    scope.Set("wait", "b", null);
    scope.TryGet("WAIT", out var second).Should().BeTrue();
    second.MatchedImageId.Should().Be("b");

    scope.Clear("wait");
    scope.TryGet("wait", out _).Should().BeFalse();
  }

  [Fact]
  public void NoScopeIsCurrentOutsideARun() {
    StepImageMatchScope.Current.Should().BeNull();
  }

  // ---------- Step result (US1) ----------

  [Theory]
  [InlineData("b", "a", "b")]
  [InlineData("e", "a", "e")]
  public async Task WaitForImageListReportsTheMatchedItem(string visible, string first, string expected) {
    var (result, _) = await RunAsync(new[] { WaitStep(List(first, "b", "c", "d", "e")) }, visible).ConfigureAwait(false);
    var detail = result.Steps.Single().WaitForImageDetails!;
    detail.ExitCondition.Should().Be("image_detected");
    detail.MatchedImageId.Should().Be(expected);
    detail.ReferenceImageId.Should().Be(first);
  }

  [Fact]
  public async Task WaitForImageListOfOneAndSingleImageReportTheirOwnId() {
    var (single, _) = await RunAsync(new[] { WaitStep(new DetectionTarget("only", 0.8)) }, "only").ConfigureAwait(false);
    single.Steps.Single().WaitForImageDetails!.MatchedImageId.Should().Be("only");

    var (one, _) = await RunAsync(new[] { WaitStep(List("solo")) }, "solo").ConfigureAwait(false);
    one.Steps.Single().WaitForImageDetails!.MatchedImageId.Should().Be("solo");
  }

  [Fact]
  public async Task WaitForImageTimeoutAndAbsentModeReportNoMatchedImage() {
    var (timeout, _) = await RunAsync(new[] { WaitStep(List("a", "b")) }).ConfigureAwait(false);
    timeout.Steps.Single().WaitForImageDetails!.ExitCondition.Should().Be("timeout_elapsed");
    timeout.Steps.Single().WaitForImageDetails!.MatchedImageId.Should().BeNull();

    var (absent, _) = await RunAsync(new[] { WaitStep(new DetectionTarget("a", 0.8), "absent") }).ConfigureAwait(false);
    absent.Steps.Single().WaitForImageDetails!.MatchedImageId.Should().BeNull();
  }

  [Fact]
  public void MatchedImageIdIsOmittedFromJsonWhenNull() {
    var json = JsonSerializer.Serialize(new WaitForImageStepResultDetails { ReferenceImageId = "a" });
    json.Should().NotContain("MatchedImageId");
    JsonSerializer.Serialize(new WaitForImageStepResultDetails { MatchedImageId = "b" }).Should().Contain("\"MatchedImageId\":\"b\"");
  }

  // ---------- Condition (US2) ----------

  [Theory]
  [InlineData("b", true)]
  [InlineData("a", false)]
  public async Task MatchedImageConditionIsTrueOnlyForTheMatchedImage(string expected, bool runs) {
    var condition = new MatchedImageStepCondition { StepRef = "wait", ImageId = expected };
    var (_, executed) = await RunAsync(
      new[] { WaitStep(List("a", "b")), CommandStep(1, "next", condition) }, "b").ConfigureAwait(false);
    executed.Contains("cmd-next").Should().Be(runs);
  }

  [Fact]
  public async Task MatchedImageConditionIsFalseWithAReasonWhenTheStepDidNotMatch() {
    var condition = new MatchedImageStepCondition { StepRef = "wait", ImageId = "a" };
    var (result, executed) = await RunAsync(
      new[] { WaitStep(List("a", "b")), CommandStep(1, "next", condition) }).ConfigureAwait(false);
    result.Status.Should().Be("Succeeded");
    executed.Should().NotContain("cmd-next");
    result.Steps[^1].Message.Should().Contain("matchedImage(stepRef=wait, expected=a) actual=none");
  }

  [Fact]
  public async Task NegateFlipsTheResultAlsoWithNoMatch() {
    var condition = new MatchedImageStepCondition { StepRef = "wait", ImageId = "a", Negate = true };
    var (_, executed) = await RunAsync(
      new[] { WaitStep(List("a", "b")), CommandStep(1, "next", condition) }).ConfigureAwait(false);
    executed.Should().Contain("cmd-next");
  }

  [Fact]
  public void ConditionRoundTripsWithTheTypeDiscriminator() {
    SequenceStepCondition condition = new MatchedImageStepCondition { StepRef = "wait", ImageId = "a", Negate = true };
    var json = JsonSerializer.Serialize(condition);
    json.Should().Contain("\"type\":\"matchedImage\"");
    var back = JsonSerializer.Deserialize<SequenceStepCondition>(json);
    back.Should().BeOfType<MatchedImageStepCondition>().Which.Should().BeEquivalentTo(condition);
    SequenceStepConditionEvaluator.Describe(condition).Should().Be("NOT matchedImage(stepRef=wait, imageId=a)");
  }

  // ---------- Guard that is an imageVisible list ----------

  [Fact]
  public async Task TopLevelImageVisibleListGuardGivesTheMatchedImageToLaterConditions() {
    var guard = new ImageVisibleStepCondition { ImageIds = new[] { "a", "b" } };
    var (result, executed) = await RunAsync(
      new[] { CommandStep(0, "guarded", guard), CommandStep(1, "next", new MatchedImageStepCondition { StepRef = "guarded", ImageId = "b" }) },
      "b").ConfigureAwait(false);

    executed.Should().Equal("cmd-guarded", "cmd-next");
    result.Steps[0].MatchedImageId.Should().Be("b");
  }

  [Fact]
  public async Task NegatedOrNestedImageVisibleGuardGivesNoMatchedImage() {
    var negated = new ImageVisibleStepCondition { ImageIds = new[] { "a", "b" }, Negate = true };
    var nested = new AllStepCondition { Children = new SequenceStepCondition[] { new ImageVisibleStepCondition { ImageIds = new[] { "b" } } } };

    var (negatedResult, _) = await RunAsync(new[] { CommandStep(0, "guarded", negated) }).ConfigureAwait(false);
    negatedResult.Steps.Single().MatchedImageId.Should().BeNull();

    var (nestedResult, nestedExecuted) = await RunAsync(
      new[] { CommandStep(0, "guarded", nested), CommandStep(1, "next", new MatchedImageStepCondition { StepRef = "guarded", ImageId = "b" }) },
      "b").ConfigureAwait(false);
    nestedExecuted.Should().Equal("cmd-guarded");
    nestedResult.Steps[0].MatchedImageId.Should().BeNull();
  }

  [Fact]
  public async Task FalseGuardGivesNoMatchedImage() {
    var guard = new ImageVisibleStepCondition { ImageIds = new[] { "a" } };
    var (result, executed) = await RunAsync(new[] { CommandStep(0, "guarded", guard) }).ConfigureAwait(false);
    executed.Should().BeEmpty();
    result.Steps.Single().MatchedImageId.Should().BeNull();
  }

  // ---------- Placeholder (US3) ----------

  [Fact]
  public void StepResultNameParsesAndBuilds() {
    StepResultPlaceholderRules.TryParse("step.wait-quest.matchedImageId", out var stepRef).Should().BeTrue();
    stepRef.Should().Be("wait-quest");
    StepResultPlaceholderRules.NameFor("wait-quest").Should().Be("step.wait-quest.matchedImageId");
    StepResultPlaceholderRules.TryParse("step.wait.other", out _).Should().BeFalse();
    StepResultPlaceholderRules.TryParse("queue.gameId", out _).Should().BeFalse();
    StepResultPlaceholderRules.IsStepResultName("step.x.y").Should().BeTrue();
  }

  [Fact]
  public void TemplateKeyAcceptsAHyphenInTheStepNameAndKeepsOldForms() {
    TemplateSubstitutor.ExtractKeys("announce-{{step.wait-quest.matchedImageId}}")
      .Should().Equal("step.wait-quest.matchedImageId");
    TemplateSubstitutor.ExtractKeys("{{iteration}} {{queue.gameId}} {{a-b}}").Should().Equal("iteration", "queue.gameId");
  }

  [Fact]
  public void ScopeResolvesThePlaceholderFromTheRunAndReportsAMissingMatch() {
    using var runScope = StepImageMatchScope.Push(new StepImageMatchScope());
    var scope = ParameterScope.Empty;
    scope.TryResolve("step.wait.matchedImageId", out _).Should().BeFalse();

    StepImageMatchScope.Current!.Set("wait", "quest-b", 0.9);
    scope.TryResolve("step.wait.matchedImageId", out var value).Should().BeTrue();
    value.Text.Should().Be("quest-b");

    TemplateSubstitutor.TrySubstitute("announce-{{step.wait.matchedImageId}}", scope.ToSubstitutionContext(), out var text, out _)
      .Should().BeTrue();
    text.Should().Be("announce-quest-b");

    StepImageMatchScope.Current.Clear("wait");
    TemplateSubstitutor.TrySubstitute("{{step.wait.matchedImageId}}", scope.ToSubstitutionContext(), out _, out var missing)
      .Should().BeFalse();
    missing.Should().Equal("step.wait.matchedImageId");
  }

  [Fact]
  public void SaveAcceptsAPriorStepReferenceAndRefusesUnknownOrLaterSteps() {
    static CommandSequence Build(string reference) {
      var sequence = new CommandSequence { Id = "s", Name = "s" };
      var later = CommandStep(1, "later");
      later.Action!.Parameters["text"] = "{{" + reference + "}}";
      sequence.SetSteps(new List<SequenceStep> { CommandStep(0, "first"), later, CommandStep(2, "last") });
      return sequence;
    }

    ParameterValidationService.ValidateSequence(Build("step.first.matchedImageId"), _ => null).Errors.Should().BeEmpty();
    ParameterValidationService.ValidateSequence(Build("step.nope.matchedImageId"), _ => null).Errors
      .Should().ContainSingle(e => e.Message.Contains("not in the sequence", StringComparison.Ordinal));
    ParameterValidationService.ValidateSequence(Build("step.last.matchedImageId"), _ => null).Errors
      .Should().ContainSingle(e => e.Message.Contains("not a prior step", StringComparison.Ordinal));
  }

  // ---------- Save rules of the condition ----------

  private static List<string> Validate(SequenceStepCondition condition, IReadOnlyDictionary<string, int>? positions = null, int? own = null) {
    var errors = new List<string>();
    CompositeConditionValidator.Validate(condition, "next", errors, positionByStepId: positions, referencingStepPosition: own);
    return errors;
  }

  [Fact]
  public void ConditionRulesRefuseEmptyFieldsUnknownAndLaterSteps() {
    var positions = new Dictionary<string, int> { ["wait"] = 0, ["next"] = 1, ["last"] = 2 };

    Validate(new MatchedImageStepCondition { StepRef = "wait", ImageId = "a" }, positions, 1).Should().BeEmpty();
    Validate(new MatchedImageStepCondition { StepRef = "", ImageId = "a" }, positions, 1)
      .Should().ContainSingle(e => e.Contains("requires stepRef", StringComparison.Ordinal));
    Validate(new MatchedImageStepCondition { StepRef = "wait", ImageId = " " }, positions, 1)
      .Should().ContainSingle(e => e.Contains("requires imageId", StringComparison.Ordinal));
    Validate(new MatchedImageStepCondition { StepRef = "nope", ImageId = "a" }, positions, 1)
      .Should().ContainSingle(e => e.Contains("unknown prior step 'nope'", StringComparison.Ordinal));
    Validate(new MatchedImageStepCondition { StepRef = "last", ImageId = "a" }, positions, 1)
      .Should().ContainSingle(e => e.Contains("must reference a prior step", StringComparison.Ordinal));
  }

  [Fact]
  public void ConditionRulesAlsoApplyInsideAComposite() {
    var composite = new AllStepCondition { Children = new SequenceStepCondition[] { new MatchedImageStepCondition { StepRef = "", ImageId = "a" } } };
    Validate(composite).Should().ContainSingle(e => e.Contains("$.children[0]", StringComparison.Ordinal) && e.Contains("requires stepRef", StringComparison.Ordinal));
  }
}
