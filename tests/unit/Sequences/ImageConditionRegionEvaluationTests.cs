using FluentAssertions;
using GameBot.Domain.Commands;
using GameBot.Domain.Commands.Blocks;
using GameBot.Domain.Services;
using Xunit;

#pragma warning disable CA2007

namespace GameBot.UnitTests.Sequences;

/// <summary>
/// Feature 130: the condition evaluator passes the region of an image condition to the image
/// adapters, and the condition text shows the region only when one is set (FR-012).
/// </summary>
public sealed class ImageConditionRegionEvaluationTests {
  private static async Task<List<Condition>> EvaluateAsync(SequenceStepCondition condition) {
    var seen = new List<Condition>();
    await SequenceStepConditionEvaluator.EvaluateAsync(
      condition,
      (c, _) => { seen.Add(c); return Task.FromResult(true); },
      new Dictionary<string, string>());
    return seen;
  }

  [Fact]
  public async Task EvaluatorCarriesThePixelRegionToTheImageAdapter() {
    var seen = await EvaluateAsync(new ImageVisibleStepCondition { ImageId = "a", Region = new PixelRegion(1, 2, 3, 4) });

    var condition = seen.Should().ContainSingle().Subject;
    condition.PixelRegion.Should().NotBeNull();
    (condition.PixelRegion!.X, condition.PixelRegion.Y, condition.PixelRegion.Width, condition.PixelRegion.Height).Should().Be((1, 2, 3, 4));
  }

  [Fact]
  public async Task EvaluatorCarriesTheRegionOfEachChildOfAComposite() {
    var composite = new AllStepCondition {
      Children = new SequenceStepCondition[] {
        new ImageVisibleStepCondition { ImageId = "a", Region = new PixelRegion(1, 2, 3, 4) },
        new ImageVisibleStepCondition { ImageId = "b" }
      }
    };

    var seen = await EvaluateAsync(composite);

    seen.Should().HaveCount(2);
    seen[0].PixelRegion.Should().NotBeNull();
    seen[1].PixelRegion.Should().BeNull();
  }

  [Fact]
  public async Task EvaluatorGivesNoRegionWhenTheConditionHasNone() {
    var seen = await EvaluateAsync(new ImageVisibleStepCondition { ImageId = "a" });

    seen.Should().ContainSingle().Which.PixelRegion.Should().BeNull();
  }

  [Fact]
  public void DescribeShowsTheRegionOnlyWhenSet() {
    var withRegion = SequenceStepConditionEvaluator.Describe(
      new ImageVisibleStepCondition { ImageId = "a", MinSimilarity = 0.9, Region = new PixelRegion(0, 400, 540, 120) });
    var without = SequenceStepConditionEvaluator.Describe(new ImageVisibleStepCondition { ImageId = "a", MinSimilarity = 0.9 });

    withRegion.Should().Be("imageVisible(imageId=a, minSimilarity=0.9, region=0,400,540,120)");
    without.Should().Be("imageVisible(imageId=a, minSimilarity=0.9)");
  }

  [Fact]
  public void DescribeShowsTheRegionOfANegatedCondition() {
    var text = SequenceStepConditionEvaluator.Describe(
      new ImageVisibleStepCondition { ImageId = "a", Negate = true, Region = new PixelRegion(1, 2, 3, 4) });

    text.Should().Be("NOT imageVisible(imageId=a, minSimilarity=default, region=1,2,3,4)");
  }

  [Fact]
  public void DescribeBreakConditionShowsTheRegionOnlyWhenSet() {
    var withRegion = SequenceRunner.DescribeBreakCondition(
      new ImageVisibleStepCondition { ImageId = "a", MinSimilarity = 0.9, Region = new PixelRegion(0, 400, 540, 120) });
    var without = SequenceRunner.DescribeBreakCondition(new ImageVisibleStepCondition { ImageId = "a", MinSimilarity = 0.9 });

    withRegion.Type.Should().Be("imageVisible");
    withRegion.Detail.Should().Be("imageVisible(imageId=a, minSimilarity=0.9, region=0,400,540,120)");
    without.Detail.Should().Be("imageVisible(imageId=a, minSimilarity=0.9)");
  }

  [Fact]
  public void CompositeTextShowsTheRegionOfAChild() {
    var composite = new AnyStepCondition {
      Children = new SequenceStepCondition[] { new ImageVisibleStepCondition { ImageId = "a", Region = new PixelRegion(1, 2, 3, 4) } }
    };

    SequenceStepConditionEvaluator.Describe(composite).Should().Be("any(imageVisible(imageId=a, minSimilarity=default, region=1,2,3,4))");
  }
}
