using FluentAssertions;
using GameBot.Domain.Actions;
using GameBot.Domain.Commands;
using Xunit;

// Test-code analyzer relaxations permitted by the constitution:
#pragma warning disable CA2007

namespace GameBot.UnitTests.Sequences;

public sealed class FileSequenceRepositoryEnsureEmulatorTests {
  [Fact]
  public async Task CreateAcceptsEnsureEmulatorRunningActionStep() {
    var root = Path.Combine(Path.GetTempPath(), $"gamebot-seq-{Guid.NewGuid():N}");
    try {
      var repo = new FileSequenceRepository(root);
      var sequence = new CommandSequence {
        Id = "ensure-emulator-seq",
        Name = "Ensure Emulator Seq"
      };
      sequence.SetSteps(new[] {
        new SequenceStep {
          StepId = "s1",
          StepType = SequenceStepType.Action,
          Action = new SequenceActionPayload { Type = ActionTypes.EnsureEmulatorRunning }
        }
      });

      var act = async () => await repo.CreateAsync(sequence);

      await act.Should().NotThrowAsync();
    }
    finally {
      if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
    }
  }
}
