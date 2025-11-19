using System.Drawing;
using GameBot.Domain.Triggers;
using DomainRegion = GameBot.Domain.Triggers.Region;
using Xunit;

namespace GameBot.Unit.Emulator.Triggers;

public sealed class ImageMatchEvaluatorTests
{
    private static Trigger CreateTrigger(double threshold) => new()
    {
        Id = "t1",
        Type = TriggerType.ImageMatch,
        Enabled = true,
        CooldownSeconds = 60,
        Params = new ImageMatchParams
        {
            ReferenceImageId = "ref1",
            Region = new DomainRegion { X = 0, Y = 0, Width = 1, Height = 1 },
            SimilarityThreshold = threshold
        }
    };

    [Fact]
    public void BelowThresholdShouldRemainPending()
    {
        var trigger = CreateTrigger(0.9);
        var evaluator = new StubImageMatchEvaluator(0.85);
        var result = evaluator.Evaluate(trigger, DateTimeOffset.UtcNow);
    Assert.Equal(TriggerStatus.Pending, result.Status);
    Assert.NotNull(result.Similarity);
    Assert.Equal(0.85, result.Similarity!.Value, 3);
    }

    [Fact]
    public void AboveThresholdShouldBeSatisfied()
    {
        var trigger = CreateTrigger(0.8);
        var evaluator = new StubImageMatchEvaluator(0.92);
        var result = evaluator.Evaluate(trigger, DateTimeOffset.UtcNow);
    Assert.Equal(TriggerStatus.Satisfied, result.Status);
    Assert.Equal(0.92, result.Similarity!.Value, 3);
    }

    // Simple stub evaluator for unit testing logic boundaries
    private sealed class StubImageMatchEvaluator : GameBot.Domain.Triggers.ITriggerEvaluator
    {
        private readonly double _similarity;
        public StubImageMatchEvaluator(double similarity) => _similarity = similarity;
        public bool CanEvaluate(Trigger trigger) => trigger.Enabled && trigger.Type == TriggerType.ImageMatch;
        public TriggerEvaluationResult Evaluate(Trigger trigger, DateTimeOffset now)
        {
            var p = (ImageMatchParams)trigger.Params;
            var status = _similarity >= p.SimilarityThreshold ? TriggerStatus.Satisfied : TriggerStatus.Pending;
            return new TriggerEvaluationResult
            {
                Status = status,
                Similarity = _similarity,
                EvaluatedAt = now,
                Reason = status == TriggerStatus.Satisfied ? "similarity_met" : "similarity_below_threshold"
            };
        }
    }
}
