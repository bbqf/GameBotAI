using FluentAssertions;
using GameBot.Domain.Actions;
using GameBot.Domain.Commands;
using Xunit;

// Test-code analyzer relaxations permitted by the constitution:
#pragma warning disable CA2007, CA1861

namespace GameBot.UnitTests.Sequences;

/// <summary>Feature 123: the repository backstop for a reschedule-self Cancel payload.</summary>
public sealed class FileSequenceRepositoryCancelPayloadTests {
  private static CommandSequence Build(params (string Key, object? Value)[] pairs) {
    var action = new SequenceActionPayload { Type = ActionTypes.RescheduleSelf };
    foreach (var (key, value) in pairs) action.Parameters[key] = value;
    var sequence = new CommandSequence { Id = "cancel-backstop", Name = "Cancel backstop" };
    sequence.SetSteps(new[] {
      new SequenceStep { StepId = "s1", StepType = SequenceStepType.Action, Action = action }
    });
    return sequence;
  }

  private static async Task WithRepoAsync(Func<FileSequenceRepository, Task> body) {
    var root = Path.Combine(Path.GetTempPath(), $"gamebot-seq-{Guid.NewGuid():N}");
    try {
      await body(new FileSequenceRepository(root));
    }
    finally {
      if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
    }
  }

  [Theory]
  [InlineData("timerTimeOfDay", "11:00")]
  [InlineData("timerRelativeOffset", "00:10:00")]
  public async Task CreateAndUpdateRejectCancelWithTimerField(string field, string value) {
    await WithRepoAsync(async repo => {
      var create = async () => await repo.CreateAsync(Build(("option", "Cancel"), (field, value)));
      (await create.Should().ThrowAsync<InvalidOperationException>()).Which.Message.Should().Contain(field);

      var update = async () => await repo.UpdateAsync(Build(("option", "Cancel"), (field, value)));
      (await update.Should().ThrowAsync<InvalidOperationException>()).Which.Message.Should().Contain(field);
    });
  }

  [Fact]
  public async Task CreateAndUpdateRejectCancelWithOcrOffset() {
    var ocr = new Dictionary<string, object?> {
      ["region"] = new Dictionary<string, object?> { ["x"] = 1, ["y"] = 2, ["width"] = 3, ["height"] = 4 },
      ["fallback"] = "00:06:00"
    };
    await WithRepoAsync(async repo => {
      var create = async () => await repo.CreateAsync(Build(("option", "Cancel"), ("ocrOffset", ocr)));
      (await create.Should().ThrowAsync<InvalidOperationException>()).Which.Message.Should().Contain("ocrOffset");

      var update = async () => await repo.UpdateAsync(Build(("option", "Cancel"), ("ocrOffset", ocr)));
      (await update.Should().ThrowAsync<InvalidOperationException>()).Which.Message.Should().Contain("ocrOffset");
    });
  }

  [Fact]
  public async Task ValidCancelPayloadSaves() {
    await WithRepoAsync(async repo => {
      var create = async () => await repo.CreateAsync(Build(("option", "Cancel")));
      await create.Should().NotThrowAsync();
      var update = async () => await repo.UpdateAsync(Build(("option", "Cancel")));
      await update.Should().NotThrowAsync();
    });
  }
}
