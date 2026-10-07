using FluentAssertions;
using GameBot.Domain.Actions;
using GameBot.Domain.Commands;
using GameBot.Domain.Commands.EnsureGameRunning;
using GameBot.Domain.Parameters;
using System.Collections.ObjectModel;
using Xunit;

// Test-code analyzer relaxations permitted by the constitution:
#pragma warning disable CA2007, CA1861

namespace GameBot.UnitTests.Sequences;

/// <summary>Feature 129: the repository backstop and the JSON round trip of the forceRestart option.</summary>
public sealed class FileSequenceRepositoryEnsureGamePayloadTests {
  private static CommandSequence Build(string id, bool hasKey, object? value = null, Collection<ParameterBinding>? bindings = null) {
    var action = new SequenceActionPayload { Type = ActionTypes.EnsureGameRunning };
    if (hasKey) action.Parameters["forceRestart"] = value;
    var sequence = new CommandSequence { Id = id, Name = id };
    sequence.SetSteps(new[] {
      new SequenceStep { StepId = "s1", StepType = SequenceStepType.Action, Action = action, ParameterBindings = bindings }
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

  // ── US2: an old stored step ──────────────────────────────────────────────

  [Fact]
  public async Task AnOldStoredStepWithoutTheKeyLoadsAndReadsAsFalse() {
    await WithRepoAsync(async repo => {
      await repo.CreateAsync(Build("old-seq", hasKey: false));

      var loaded = await repo.GetAsync("old-seq");

      var action = loaded!.Steps.Single().Action!;
      action.Parameters.Should().NotContainKey("forceRestart");
      EnsureGameRunningPayload.TryRead(action, out var value, out var error).Should().BeTrue();
      value.Should().BeFalse();
      error.Should().BeNull();
    });
  }

  // ── US5: guard and round trip ─────────────────────────────────────────────

  [Theory]
  [InlineData("true")]
  [InlineData(1)]
  [InlineData("{{x}}")]
  [InlineData(null)]
  public async Task TheGuardThrowsForABadValueOnCreateAndUpdate(object? bad) {
    await WithRepoAsync(async repo => {
      var create = async () => await repo.CreateAsync(Build("bad-seq", hasKey: true, bad));
      (await create.Should().ThrowAsync<InvalidOperationException>()).Which.Message.Should().Contain("forceRestart");

      var update = async () => await repo.UpdateAsync(Build("bad-seq", hasKey: true, bad));
      (await update.Should().ThrowAsync<InvalidOperationException>()).Which.Message.Should().Contain("forceRestart");
    });
  }

  [Theory]
  [InlineData(true)]
  [InlineData(false)]
  public async Task ASavedValueIsTheSameAfterTheReadFromTheFile(bool value) {
    await WithRepoAsync(async repo => {
      await repo.CreateAsync(Build("round-seq", hasKey: true, value));

      var loaded = await repo.GetAsync("round-seq");

      var action = loaded!.Steps.Single().Action!;
      EnsureGameRunningPayload.TryRead(action, out var read, out _).Should().BeTrue();
      read.Should().Be(value);
    });
  }

  [Fact]
  public async Task AStepParameterBindingThatTargetsForceRestartIsRejected() {
    var bindings = new Collection<ParameterBinding> { new() { Name = "forceRestart", Value = "true" } };
    await WithRepoAsync(async repo => {
      var create = async () => await repo.CreateAsync(Build("bind-seq", hasKey: false, bindings: bindings));
      (await create.Should().ThrowAsync<InvalidOperationException>()).Which.Message.Should().Contain("forceRestart");
    });
  }

  [Fact]
  public async Task AStepParameterBindingForAnotherNameStillSaves() {
    var bindings = new Collection<ParameterBinding> { new() { Name = "other", Value = "x" } };
    await WithRepoAsync(async repo => {
      var create = async () => await repo.CreateAsync(Build("other-bind-seq", hasKey: false, bindings: bindings));
      await create.Should().NotThrowAsync();
    });
  }
}
