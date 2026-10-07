using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using GameBot.Domain.Actions;
using GameBot.Domain.Commands;
using GameBot.Domain.Parameters;
using GameBot.Service.Services.EnsureGameRunning;
using GameBot.Service.Services.SequenceExecution;
using Xunit;

// Test-code analyzer relaxations permitted by the constitution:
#pragma warning disable CA2007, CA1861, CA1859

namespace GameBot.UnitTests.Domain;

/// <summary>Feature 129 (FR-016, SC-008): the typed variant keeps forceRestart across save, read, and run.</summary>
public sealed class PrimitiveEnsureGameRunningActionRoundTripTests {
  private sealed class FakeHandler : IEnsureGameRunningActionHandler {
    public int ExecuteCalls { get; private set; }
    public int RestartCalls { get; private set; }

    public Task<EnsureGameRunningActionResult> ExecuteAsync(string sessionId, CancellationToken ct = default) {
      ExecuteCalls++;
      return Task.FromResult(new EnsureGameRunningActionResult(EnsureGameRunningOutcome.GameRunning));
    }

    public Task<EnsureGameRunningActionResult> RestartAsync(string sessionId, CancellationToken ct = default) {
      RestartCalls++;
      return Task.FromResult(new EnsureGameRunningActionResult(EnsureGameRunningOutcome.Restarted));
    }
  }

  [Theory]
  [InlineData(true)]
  [InlineData(false)]
  public void TheTypedVariantKeepsTheValueThroughJson(bool value) {
    var action = new PrimitiveEnsureGameRunningAction { ForceRestart = value };

    var json = JsonSerializer.Serialize(action);
    var back = JsonSerializer.Deserialize<PrimitiveEnsureGameRunningAction>(json);

    back!.ForceRestart.Should().Be(value);
  }

  [Fact]
  public void ANullValueIsNotWrittenToJson() {
    var json = JsonSerializer.Serialize(new PrimitiveEnsureGameRunningAction());

    json.Should().NotContain("ForceRestart");
    JsonSerializer.Deserialize<PrimitiveEnsureGameRunningAction>(json)!.ForceRestart.Should().BeNull();
  }

  [Theory]
  [InlineData(true)]
  [InlineData(false)]
  [InlineData(null)]
  public void ToActionPayloadAndTryFromActionPayloadAgree(bool? value) {
    var action = new PrimitiveEnsureGameRunningAction { ForceRestart = value };

    var payload = action.ToActionPayload();
    PrimitiveEnsureGameRunningAction.TryFromActionPayload(payload, out var back, out var error).Should().BeTrue();

    payload.Type.Should().Be(ActionTypes.EnsureGameRunning);
    payload.Parameters.ContainsKey("forceRestart").Should().Be(value.HasValue);
    back!.ForceRestart.Should().Be(value);
    error.Should().BeNull();
  }

  [Fact]
  public void TryFromActionPayloadRejectsABadValue() {
    var payload = new SequenceActionPayload { Type = ActionTypes.EnsureGameRunning };
    payload.Parameters["forceRestart"] = "yes";

    PrimitiveEnsureGameRunningAction.TryFromActionPayload(payload, out var back, out var error).Should().BeFalse();

    back.Should().BeNull();
    error.Should().Contain("forceRestart");
  }

  [Fact]
  public async Task TheFullChainFromTypedVariantToTheRestartCall() {
    var root = Path.Combine(Path.GetTempPath(), $"gamebot-seq-{Guid.NewGuid():N}");
    try {
      var repo = new FileSequenceRepository(root);
      var typed = new PrimitiveEnsureGameRunningAction { ForceRestart = true };
      var sequence = new CommandSequence { Id = "chain-seq", Name = "chain" };
      sequence.SetSteps(new[] {
        new SequenceStep { StepId = "s1", StepType = SequenceStepType.Action, Action = typed.ToActionPayload() }
      });
      await repo.CreateAsync(sequence);

      var loaded = await repo.GetAsync("chain-seq");
      var handler = new FakeHandler();
      var service = new SequenceExecutionService(
        runner: null!, evalSvc: null!, imageVisibleConditionAdapter: null!, imageRepository: null!,
        executionLogService: null!, commandRepository: null!, sequenceRepository: null!, commandExecutor: null!,
        selfRescheduleCoordinator: null!, sessionManager: null!, ensureGameRunning: handler,
        ensureEmulatorRunning: null!, sessionService: null!, ocrOffsetResolver: null!);

      var result = await service.DispatchActionAsync(
        loaded!.Steps.Single().Action!, "chain-seq", null, "s1", ParameterScope.Empty, null, CancellationToken.None);

      handler.RestartCalls.Should().Be(1);
      handler.ExecuteCalls.Should().Be(0);
      result.Outcome.Should().Be("restarted");
    }
    finally {
      if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
    }
  }
}
