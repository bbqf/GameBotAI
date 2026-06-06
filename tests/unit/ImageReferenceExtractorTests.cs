using System.Collections.ObjectModel;
using FluentAssertions;
using GameBot.Domain.Commands;
using GameBot.Service.Services;
using Xunit;

namespace GameBot.UnitTests;

public sealed class ImageReferenceExtractorTests {
  [Fact]
  public void ExtractImageIds_CommandWithDetection_ReturnsId() {
    var cmd = new Command {
      Id = "cmd1", Name = "C",
      Detection = new DetectionTarget("img-detect", 0.8, 0, 0, DetectionSelectionStrategy.HighestConfidence)
    };
    ImageReferenceExtractor.ExtractImageIds(cmd).Should().Contain("img-detect");
  }

  [Fact]
  public void ExtractImageIds_CommandWithPrimitiveTapStep_ReturnsId() {
    var cmd = new Command {
      Id = "cmd1", Name = "C",
      Steps = new Collection<CommandStep> {
        new CommandStep {
          Type = CommandStepType.PrimitiveTap, Order = 0,
          PrimitiveTap = new PrimitiveTapConfig {
            DetectionTarget = new DetectionTarget("img-tap", 0.9, 0, 0, DetectionSelectionStrategy.HighestConfidence)
          }
        }
      }
    };
    ImageReferenceExtractor.ExtractImageIds(cmd).Should().Contain("img-tap");
  }

  [Fact]
  public void ExtractImageIds_CommandWithWaitForImageStep_ReturnsId() {
    var cmd = new Command {
      Id = "cmd1", Name = "C",
      Steps = new Collection<CommandStep> {
        new CommandStep {
          Type = CommandStepType.WaitForImage, Order = 0,
          WaitForImage = new WaitForImageConfig {
            DetectionTarget = new DetectionTarget("img-wait", 0.8, 0, 0, DetectionSelectionStrategy.HighestConfidence)
          }
        }
      }
    };
    ImageReferenceExtractor.ExtractImageIds(cmd).Should().Contain("img-wait");
  }

  [Fact]
  public void ExtractImageIds_CommandWithMultipleStepTypes_ReturnsAllIds() {
    var cmd = new Command {
      Id = "cmd1", Name = "C",
      Detection = new DetectionTarget("img-d", 0.8, 0, 0, DetectionSelectionStrategy.HighestConfidence),
      Steps = new Collection<CommandStep> {
        new CommandStep {
          Type = CommandStepType.PrimitiveTap, Order = 0,
          PrimitiveTap = new PrimitiveTapConfig {
            DetectionTarget = new DetectionTarget("img-tap", 0.9, 0, 0, DetectionSelectionStrategy.HighestConfidence)
          }
        },
        new CommandStep {
          Type = CommandStepType.WaitForImage, Order = 1,
          WaitForImage = new WaitForImageConfig {
            DetectionTarget = new DetectionTarget("img-wait", 0.8, 0, 0, DetectionSelectionStrategy.HighestConfidence)
          }
        }
      }
    };
    var ids = ImageReferenceExtractor.ExtractImageIds(cmd).ToList();
    ids.Should().Contain("img-d");
    ids.Should().Contain("img-tap");
    ids.Should().Contain("img-wait");
  }

  [Fact]
  public void ExtractImageIds_CommandWithNoImageRefs_ReturnsEmpty() {
    var cmd = new Command {
      Id = "cmd1", Name = "C",
      Steps = new Collection<CommandStep> {
        new CommandStep { Type = CommandStepType.EnsureGameRunning, Order = 0 }
      }
    };
    ImageReferenceExtractor.ExtractImageIds(cmd).Should().BeEmpty();
  }

  [Fact]
  public void ExtractImageIds_SequenceStepsWithGate_ReturnsGateTargetId() {
    var steps = new List<SequenceStep> {
      new SequenceStep { Gate = new GateConfig { TargetId = "img-gate" } }
    };
    ImageReferenceExtractor.ExtractImageIds(steps).Should().Contain("img-gate");
  }

  [Fact]
  public void ExtractImageIds_SequenceStepsWithWaitForImage_ReturnsId() {
    var steps = new List<SequenceStep> {
      new SequenceStep {
        WaitForImage = new WaitForImageConfig {
          DetectionTarget = new DetectionTarget("img-seq-wait", 0.8, 0, 0, DetectionSelectionStrategy.HighestConfidence)
        }
      }
    };
    ImageReferenceExtractor.ExtractImageIds(steps).Should().Contain("img-seq-wait");
  }

  [Fact]
  public void ExtractImageIds_SequenceLoopBody_RecursesIntoBody() {
    var loopStep = new SequenceStep {
      StepType = SequenceStepType.Loop,
      Body = new List<SequenceStep> {
        new SequenceStep { Gate = new GateConfig { TargetId = "img-loop-body" } }
      }
    };
    ImageReferenceExtractor.ExtractImageIds(new[] { loopStep }).Should().Contain("img-loop-body");
  }

  [Fact]
  public void ExtractImageIds_EmptySequenceSteps_ReturnsEmpty() {
    ImageReferenceExtractor.ExtractImageIds(Array.Empty<SequenceStep>()).Should().BeEmpty();
  }
}
