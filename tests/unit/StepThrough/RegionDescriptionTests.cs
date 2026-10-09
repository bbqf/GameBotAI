using FluentAssertions;
using GameBot.Domain.Commands;
using GameBot.Domain.Services;
using GameBot.Domain.Services.StepThrough;
using Xunit;
using static GameBot.UnitTests.StepThrough.StepperTestKit;

#pragma warning disable CA2007, CA1861, CA1859, CA1849

namespace GameBot.UnitTests.StepThrough;

/// <summary>Feature 130 (FR-012): the step-through output shows the region of an image condition.</summary>
public sealed class RegionDescriptionTests {
  private static SequenceStep IfWith(ImageVisibleStepCondition condition) {
    var step = If(0, "if", new[] { Cmd(0, "then1") });
    step.If = new IfConfig { Condition = condition };
    return step;
  }

  private static async Task<string> EnterMessageAsync(ImageVisibleStepCondition condition) {
    var sequence = Sequence(IfWith(condition));
    var state = Start(sequence);
    var stepper = new SequenceStepper(Runner(sequence));

    await RunToEndAsync(stepper, sequence, state, Deps(new List<string>(), Answers((condition.ImageId, new[] { true }))));

    return state.History.Single(h => h.Kind == HistoryKind.Enter).Message!;
  }

  [Fact]
  public async Task IfConditionWithARegionShowsTheRegion() {
    var message = await EnterMessageAsync(new ImageVisibleStepCondition {
      ImageId = "price-icon",
      MinSimilarity = 0.9,
      Region = new PixelRegion(0, 400, 540, 120)
    });

    message.Should().Contain("imageVisible(imageId=price-icon, minSimilarity=0.9, region=0,400,540,120)");
  }

  [Fact]
  public async Task IfConditionWithNoRegionShowsTheOldText() {
    var message = await EnterMessageAsync(new ImageVisibleStepCondition { ImageId = "price-icon" });

    message.Should().Contain("imageVisible(imageId=price-icon, minSimilarity=default)");
    message.Should().NotContain("region");
  }
}
