#pragma warning disable CA1861 // Test data arrays are small and used one time.
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using GameBot.Domain.Commands;
using GameBot.Domain.Parameters;
using GameBot.Domain.Services;
using Xunit;

namespace GameBot.UnitTests.Sequences;

/// <summary>Feature 180: the rules, the evaluator, and the resolver for an image list in a condition.</summary>
public sealed class ImageListConditionTests {
  private static readonly ParameterDeclaration Quests = new() {
    Name = "quests", Kind = ParameterInputKind.Image, AllowedImages = new[] { "a", "b", "c" }, Multiple = true
  };

  private static ParameterScope ScopeWithList(params string[] items) {
    var bindings = new Collection<ParameterBinding> {
      new() { Name = "quests", Value = ParameterListRules.Join(items), Values = items }
    };
    return ParameterScope.Empty.Child(ParameterScopeLayers.Entry, bindings, new[] { Quests });
  }

  private static async Task<(bool Value, List<string> Asked)> RunAsync(SequenceStepCondition condition, params string[] visible) {
    var asked = new List<string>();
    var result = await SequenceStepConditionEvaluator.EvaluateAsync(
      condition,
      (c, _) => {
        asked.Add(c.TargetId);
        return Task.FromResult(visible.Contains(c.TargetId));
      },
      new Dictionary<string, string>(),
      CancellationToken.None).ConfigureAwait(false);
    return (result.Value, asked);
  }

  // ---------- ImageListRules ----------

  [Fact]
  public void NormalizeKeepsFirstOccurrenceAndOrderAndDropsEmptyItems() {
    ImageListRules.Normalize(new[] { "b", "a", "", "b", " ", "c", "a" }).Should().Equal("b", "a", "c");
  }

  [Fact]
  public void WrittenListRulesRefuseBothFieldsAnEmptyItemTooManyItemsAndAnotherMatch() {
    ImageListRules.TryValidateWritten("a", new[] { "b" }, null, "x", out var both).Should().BeFalse();
    both.Should().ContainSingle(e => e.Contains("both", System.StringComparison.Ordinal));

    ImageListRules.TryValidateWritten("", new[] { "b", "" }, null, "x", out var empty).Should().BeFalse();
    empty.Should().ContainSingle(e => e.Contains("empty item", System.StringComparison.Ordinal));

    var many = Enumerable.Range(0, ImageListRules.MaxItems + 1).Select(i => "i" + i).ToArray();
    ImageListRules.TryValidateWritten("", many, null, "x", out var tooMany).Should().BeFalse();
    tooMany.Should().ContainSingle(e => e.Contains("at most", System.StringComparison.Ordinal));

    ImageListRules.TryValidateWritten("", Array.Empty<string>(), null, "x", out var none).Should().BeFalse();
    none.Should().NotBeEmpty();

    ImageListRules.TryValidateWritten("a", null, "all", "x", out var match).Should().BeFalse();
    match.Should().ContainSingle(e => e.Contains("match", System.StringComparison.Ordinal));

    ImageListRules.TryValidateWritten("", new[] { "a", "b" }, "any", "x", out _).Should().BeTrue();
    ImageListRules.TryValidateWritten("a", null, "any", "x", out _).Should().BeTrue();
  }

  // ---------- Evaluator ----------

  [Fact]
  public async Task AnyVisibleItemGivesTrueAndStopsAtTheFirstMatch() {
    var condition = new ImageVisibleStepCondition { ImageIds = new[] { "a", "b", "c" } };
    var (value, asked) = await RunAsync(condition, "b", "c").ConfigureAwait(false);
    value.Should().BeTrue();
    asked.Should().Equal("a", "b");
  }

  [Fact]
  public async Task NoVisibleItemGivesFalse() {
    var (value, asked) = await RunAsync(new ImageVisibleStepCondition { ImageIds = new[] { "a", "b" } }).ConfigureAwait(false);
    value.Should().BeFalse();
    asked.Should().Equal("a", "b");
  }

  [Fact]
  public async Task EmptyListGivesFalseAndNegateFlipsIt() {
    (await RunAsync(new ImageVisibleStepCondition { ImageIds = Array.Empty<string>() }).ConfigureAwait(false)).Value.Should().BeFalse();
    (await RunAsync(new ImageVisibleStepCondition { ImageIds = Array.Empty<string>(), Negate = true }).ConfigureAwait(false)).Value.Should().BeTrue();
    (await RunAsync(new ImageVisibleStepCondition { ImageIds = new[] { "a" }, Negate = true }, "a").ConfigureAwait(false)).Value.Should().BeFalse();
  }

  [Fact]
  public async Task OneImageConditionIsUnchanged() {
    var (value, asked) = await RunAsync(new ImageVisibleStepCondition { ImageId = "a" }, "a").ConfigureAwait(false);
    value.Should().BeTrue();
    asked.Should().Equal("a");
  }

  [Fact]
  public void DescribeShowsTheListAndKeepsTheOneImageText() {
    SequenceStepConditionEvaluator.Describe(new ImageVisibleStepCondition { ImageIds = new[] { "a", "b" } })
      .Should().Contain("imageIds=[a, b]");
    SequenceStepConditionEvaluator.Describe(new ImageVisibleStepCondition { ImageId = "a" })
      .Should().Contain("imageId=a");
  }

  // ---------- Resolver ----------

  [Fact]
  public void ListPlaceholderExpandsToImageIdsAndTheStoredConditionDoesNotChange() {
    var condition = new ImageVisibleStepCondition { ImageId = "{{quests}}", MinSimilarity = 0.9, Negate = true };

    SequenceStepConditionResolver.TryResolve(condition, ScopeWithList("a", "b"), "condition", out var resolved, out var error, out var used)
      .Should().BeTrue();

    error.Should().BeNull();
    var image = resolved.Should().BeOfType<ImageVisibleStepCondition>().Subject;
    image.ImageIds.Should().Equal("a", "b");
    image.Match.Should().Be(ImageListRules.MatchAny);
    image.MinSimilarity.Should().Be(0.9);
    image.Negate.Should().BeTrue();
    condition.ImageId.Should().Be("{{quests}}");
    condition.ImageIds.Should().BeNull();
    used.Should().ContainSingle(p => p.Name == "quests");
  }

  [Fact]
  public void ListOfOneItemGivesAListOfOne() {
    SequenceStepConditionResolver.TryResolve(new ImageVisibleStepCondition { ImageId = "{{quests}}" }, ScopeWithList("c"), "condition", out var resolved, out _, out _)
      .Should().BeTrue();
    resolved.Should().BeOfType<ImageVisibleStepCondition>().Which.ImageIds.Should().Equal("c");
  }

  [Fact]
  public void SingleValuePlaceholderKeepsTheOldResult() {
    var scope = ParameterScope.Empty.Child(
      ParameterScopeLayers.Entry, new Collection<ParameterBinding> { new() { Name = "one", Value = "x" } }, null);
    SequenceStepConditionResolver.TryResolve(new ImageVisibleStepCondition { ImageId = "{{one}}" }, scope, "condition", out var resolved, out _, out _)
      .Should().BeTrue();
    var image = resolved.Should().BeOfType<ImageVisibleStepCondition>().Subject;
    image.ImageId.Should().Be("x");
    image.ImageIds.Should().BeNull();
  }

  [Fact]
  public void PlaceholderInsideTextKeepsTheJoinedTextRule() {
    SequenceStepConditionResolver.TryResolve(new ImageVisibleStepCondition { ImageId = "x_{{quests}}" }, ScopeWithList("a", "b"), "condition", out var resolved, out _, out _)
      .Should().BeTrue();
    var image = resolved.Should().BeOfType<ImageVisibleStepCondition>().Subject;
    image.ImageId.Should().Be("x_a|b");
    image.ImageIds.Should().BeNull();
  }

  [Fact]
  public void PlaceholderItemsOfAWrittenListResolveAsSingleValues() {
    var scope = ParameterScope.Empty.Child(
      ParameterScopeLayers.Entry, new Collection<ParameterBinding> { new() { Name = "one", Value = "x" } }, null);
    var condition = new ImageVisibleStepCondition { ImageIds = new[] { "{{one}}", "y" } };
    SequenceStepConditionResolver.TryResolve(condition, scope, "condition", out var resolved, out _, out _).Should().BeTrue();
    resolved.Should().BeOfType<ImageVisibleStepCondition>().Which.ImageIds.Should().Equal("x", "y");
  }

  [Fact]
  public void UnknownListPlaceholderGivesAnError() {
    SequenceStepConditionResolver.TryResolve(new ImageVisibleStepCondition { ImageId = "{{nope}}" }, ScopeWithList("a"), "condition", out _, out var error, out _)
      .Should().BeFalse();
    error!.Reason.Should().Be(ParameterResolutionReasons.Unresolved);
  }

  // ---------- Save check ----------

  [Fact]
  public void ListPlaceholderIsAllowedInTheConditionAndRefusedInATapImage() {
    var sequence = new CommandSequence { Id = "s", Name = "s" };
    sequence.Parameters.Add(Quests);
    sequence.SetSteps(new List<SequenceStep> {
      new() { StepId = "one", Order = 0, CommandId = "c", Condition = new ImageVisibleStepCondition { ImageId = "{{quests}}" } }
    });

    var ok = ParameterValidationService.ValidateSequence(sequence, _ => null);
    ok.Errors.Should().BeEmpty();
    ok.Warnings.Should().Contain(w => w.Code == ParameterValidationCodes.StaticCheckSkipped);

    var command = new Command { Id = "c", Name = "c" };
    command.Parameters.Add(Quests);
    command.Steps.Add(new CommandStep {
      Type = CommandStepType.PrimitiveTap, Order = 1,
      PrimitiveTap = new PrimitiveTapConfig { DetectionTarget = new DetectionTarget("{{quests}}") }
    });
    command.Steps.Add(new CommandStep {
      Type = CommandStepType.WaitForImage, Order = 2,
      WaitForImage = new WaitForImageConfig { DetectionTarget = new DetectionTarget("{{quests}}") }
    });
    command.Steps.Add(new CommandStep {
      Type = CommandStepType.EnsureEmulatorRunning, Order = 3,
      EnsureEmulatorRunning = new EnsureEmulatorRunningConfig { AdbSerial = "{{quests}}" }
    });

    var bad = ParameterValidationService.ValidateCommand(command);
    bad.Errors.Where(e => e.Code == ParameterValidationCodes.ListInSingleField)
      .Select(e => e.FieldPath).Should().BeEquivalentTo(
        "primitiveTap.detectionTarget.referenceImageId", "ensureEmulatorRunning.adbSerial");
    bad.Errors.Should().OnlyContain(e => e.ParameterName == "quests" && e.Message.Contains("quests", System.StringComparison.Ordinal));
  }

  [Fact]
  public void SingleValueParameterAndTextPlaceholderAreNotRefused() {
    var single = new ParameterDeclaration { Name = "one", Kind = ParameterInputKind.Image, AllowedImages = new[] { "a" } };
    var command = new Command { Id = "c", Name = "c" };
    command.Parameters.Add(single);
    command.Parameters.Add(Quests);
    command.Steps.Add(new CommandStep {
      Type = CommandStepType.PrimitiveTap, Order = 1,
      PrimitiveTap = new PrimitiveTapConfig { DetectionTarget = new DetectionTarget("{{one}}") }
    });
    command.Steps.Add(new CommandStep {
      Type = CommandStepType.PrimitiveTap, Order = 2,
      PrimitiveTap = new PrimitiveTapConfig { DetectionTarget = new DetectionTarget("x_{{quests}}") }
    });

    ParameterValidationService.ValidateCommand(command).Errors.Should().BeEmpty();
  }

  [Fact]
  public void ListInAnItemOfAWrittenListIsRefused() {
    var command = new Command { Id = "c", Name = "c" };
    command.Parameters.Add(Quests);
    command.Steps.Add(new CommandStep {
      Type = CommandStepType.WaitForImage, Order = 1,
      WaitForImage = new WaitForImageConfig {
        DetectionTarget = new DetectionTarget("a", 0.8, 0, 0, DetectionSelectionStrategy.HighestConfidence, null, new[] { "a", "{{quests}}" })
      }
    });

    ParameterValidationService.ValidateCommand(command).Errors
      .Should().ContainSingle(e => e.Code == ParameterValidationCodes.ListInSingleField
        && e.FieldPath == "waitForImage.detectionTarget.referenceImageIds[1]");
  }

  // ---------- Command step resolver ----------

  [Fact]
  public void WaitForImageExpandsAListPlaceholderInlineAndInTheOverlay() {
    var inline = new CommandStep {
      Type = CommandStepType.WaitForImage, Order = 1,
      WaitForImage = new WaitForImageConfig { DetectionTarget = new DetectionTarget("{{quests}}") }
    };
    CommandStepResolver.TryResolve(inline, ScopeWithList("b", "a"), out var resolved, out var error, out _).Should().BeTrue();
    error.Should().BeNull();
    var target = resolved!.WaitForImage!.DetectionTarget!;
    target.ReferenceImageIds.Should().Equal("b", "a");
    target.ReferenceImageId.Should().Be("b");

    var overlay = new CommandStep {
      Type = CommandStepType.WaitForImage, Order = 1,
      WaitForImage = new WaitForImageConfig { DetectionTarget = new DetectionTarget("fallback") },
      FieldTemplates = new Dictionary<string, string> { ["waitForImage.detectionTarget.referenceImageId"] = "{{quests}}" }
    };
    CommandStepResolver.TryResolve(overlay, ScopeWithList("c", "a"), out var overlaid, out _, out _).Should().BeTrue();
    overlaid!.WaitForImage!.DetectionTarget!.ReferenceImageIds.Should().Equal("c", "a");
  }

  [Fact]
  public void WaitForImageKeepsSingleValueAndOtherStepsStillRefuseAList() {
    var scope = ParameterScope.Empty.Child(
      ParameterScopeLayers.Entry, new Collection<ParameterBinding> { new() { Name = "one", Value = "x" } }, null);
    var wait = new CommandStep {
      Type = CommandStepType.WaitForImage, Order = 1,
      WaitForImage = new WaitForImageConfig { DetectionTarget = new DetectionTarget("{{one}}") }
    };
    CommandStepResolver.TryResolve(wait, scope, out var resolved, out _, out _).Should().BeTrue();
    resolved!.WaitForImage!.DetectionTarget!.ReferenceImageId.Should().Be("x");
    resolved.WaitForImage.DetectionTarget.ReferenceImageIds.Should().BeNull();

    var tap = new CommandStep {
      Type = CommandStepType.PrimitiveTap, Order = 1,
      PrimitiveTap = new PrimitiveTapConfig { DetectionTarget = new DetectionTarget("{{quests}}") }
    };
    CommandStepResolver.TryResolve(tap, ScopeWithList("a", "b"), out _, out var error, out _).Should().BeFalse();
    error!.Reason.Should().Be(ParameterResolutionReasons.ListInSingleField);
  }
}
