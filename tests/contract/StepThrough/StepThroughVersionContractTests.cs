#pragma warning disable CA2007, CA1861, CA1859, CA1849, CA1054, CA1056, CA1307, CA1308
using System.Threading.Tasks;
using FluentAssertions;
using GameBot.Domain.Commands;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using static GameBot.ContractTests.StepThrough.StepThroughFixture;

namespace GameBot.ContractTests.StepThrough;

/// <summary>A step-through uses the stored version of a sequence (feature 127, FR-013, SC-005).</summary>
public sealed class StepThroughVersionContractTests : IClassFixture<StepThroughFixture> {
  private readonly StepThroughFixture _f;

  public StepThroughVersionContractTests(StepThroughFixture fixture) { _f = fixture; }

  private static string Error(System.Text.Json.JsonElement body) => body.GetProperty("error").GetProperty("code").GetString()!;

  [Fact]
  public async Task AChangeOfTheStoredSequenceMakesRunNextReturn409SequenceChanged() {
    var sequenceId = await _f.SeedSequenceAsync("two", Tap(0, "a"), Tap(1, "b"));
    var id = await _f.StartAsync(sequenceId, _f.NewGameSession());
    await _f.ReplaceSequenceAsync(sequenceId, Tap(0, "a"), Tap(1, "b"), Tap(2, "c"));

    var (status, body) = await _f.PostAsync($"/api/step-through/{id}/run-next");

    status.Should().Be(409);
    Error(body).Should().Be("sequence_changed");
    body.GetProperty("error").GetProperty("message").GetString().Should().Contain("Restart");
  }

  [Fact]
  public async Task AChangeOfTheStoredSequenceMakesSelectReturn409SequenceChanged() {
    var sequenceId = await _f.SeedSequenceAsync("two", Tap(0, "a"), Tap(1, "b"));
    var id = await _f.StartAsync(sequenceId, _f.NewGameSession());
    await _f.ReplaceSequenceAsync(sequenceId, Tap(0, "a"));

    var (status, body) = await _f.PostAsync($"/api/step-through/{id}/select", new { path = "1" });

    status.Should().Be(409);
    Error(body).Should().Be("sequence_changed");
  }

  [Fact]
  public async Task RestartTakesTheNewVersionAndTheAuthorCanGoOn() {
    var sequenceId = await _f.SeedSequenceAsync("two", Tap(0, "a"), Tap(1, "b"));
    var id = await _f.StartAsync(sequenceId, _f.NewGameSession());
    await _f.ReplaceSequenceAsync(sequenceId, Tap(0, "a"), Tap(1, "b"), Tap(2, "c"));
    (await _f.PostAsync($"/api/step-through/{id}/run-next")).Status.Should().Be(409);

    var (restartStatus, restarted) = await _f.PostAsync($"/api/step-through/{id}/restart");

    restartStatus.Should().Be(200);
    restarted.GetProperty("nodes").GetArrayLength().Should().Be(3);
    (await _f.PostAsync($"/api/step-through/{id}/run-next")).Status.Should().Be(202);
    await _f.WaitIdleAsync(id);
  }

  [Fact]
  public async Task ASequenceWithNoStepsReturns400SequenceEmpty() {
    var sequenceId = await _f.SeedSequenceAsync("empty");

    var (status, body) = await _f.PostAsync("/api/step-through", new { sequenceId, gameSessionId = _f.NewGameSession() });

    status.Should().Be(400);
    Error(body).Should().Be("sequence_empty");
  }

  [Fact]
  public async Task AFlowGraphSequenceReturns400UnsupportedSequenceKind() {
    var sequence = new CommandSequence { Id = "seq-flow-" + System.Guid.NewGuid().ToString("N")[..6], Name = "flow", EntryStepId = "s1" };
    sequence.SetFlowSteps(new[] { new FlowStep { StepId = "s1", Label = "one", StepType = FlowStepType.Terminal } });
    await _f.Services.GetRequiredService<ISequenceRepository>().CreateAsync(sequence);

    var (status, body) = await _f.PostAsync("/api/step-through", new { sequenceId = sequence.Id, gameSessionId = _f.NewGameSession() });

    status.Should().Be(400);
    Error(body).Should().Be("unsupported_sequence_kind");
  }

  [Fact]
  public async Task ADeletedSequenceMakesRunNextReturn409SequenceChanged() {
    var sequenceId = await _f.SeedSequenceAsync("two", Tap(0, "a"), Tap(1, "b"));
    var id = await _f.StartAsync(sequenceId, _f.NewGameSession());
    await _f.Services.GetRequiredService<ISequenceRepository>().DeleteAsync(sequenceId);

    var (status, body) = await _f.PostAsync($"/api/step-through/{id}/run-next");

    status.Should().Be(409);
    Error(body).Should().Be("sequence_changed");
  }
}
