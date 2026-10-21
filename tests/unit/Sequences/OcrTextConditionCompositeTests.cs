using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using GameBot.Domain.Commands;
using GameBot.Domain.Services;
using Xunit;
using static GameBot.UnitTests.Sequences.OcrTextTestKit;

#pragma warning disable CA2007

namespace GameBot.UnitTests.Sequences;

/// <summary>The ocrText leaf as a child of all, any and none (issue #338, User Story 2).</summary>
public sealed class OcrTextConditionCompositeTests {
  private static Task<ConditionEvaluation> RunAsync(SequenceStepCondition condition, bool imageVisible = true)
    => SequenceStepConditionEvaluator.EvaluateAsync(
      condition,
      (_, _) => Task.FromResult(imageVisible),
      new Dictionary<string, string> { ["earlier"] = "success" },
      lastRunEvaluator: (_, _) => Task.FromResult(true),
      CancellationToken.None);

  private static SequenceStepCondition[] Children(bool ocrMatches) => new SequenceStepCondition[] {
    new ImageVisibleStepCondition { ImageId = "x" },
    new CommandOutcomeStepCondition { StepRef = "earlier", ExpectedState = "success" },
    new LastRunStepCondition { Sequence = "self", Status = "success", Within = "01:00:00" },
    Expect(ocrMatches ? "0" : "9")
  };

  [Theory]
  [InlineData(true, true)]
  [InlineData(false, false)]
  public async Task AllNeedsTheOcrChildToo(bool ocrMatches, bool expected) {
    using (OcrTextEvaluationScope.Push(TextScope("0"))) {
      (await RunAsync(new AllStepCondition { Children = Children(ocrMatches) })).Value.Should().Be(expected);
    }
  }

  [Fact]
  public async Task AnyIsTrueWhenOnlyTheOcrChildIsTrue() {
    using (OcrTextEvaluationScope.Push(TextScope("0"))) {
      var children = new SequenceStepCondition[] { new ImageVisibleStepCondition { ImageId = "x" }, Expect("0") };

      (await RunAsync(new AnyStepCondition { Children = children }, imageVisible: false)).Value.Should().BeTrue();
    }
  }

  [Fact]
  public async Task NoneIsFalseWhenTheOcrChildIsTrue() {
    using (OcrTextEvaluationScope.Push(TextScope("0"))) {
      var children = new SequenceStepCondition[] { Expect("0") };

      (await RunAsync(new NoneStepCondition { Children = children })).Value.Should().BeFalse();
      (await RunAsync(new NoneStepCondition { Children = new SequenceStepCondition[] { Expect("5") } })).Value.Should().BeTrue();
    }
  }

  [Fact]
  public async Task ASkippedChildGivesNoRecord() {
    var scope = TextScope("0");
    using (OcrTextEvaluationScope.Push(scope)) {
      var children = new SequenceStepCondition[] { new ImageVisibleStepCondition { ImageId = "x" }, Expect("0") };

      await RunAsync(new AllStepCondition { Children = children }, imageVisible: false);
    }

    scope.Drain().Should().BeEmpty();
  }
}
