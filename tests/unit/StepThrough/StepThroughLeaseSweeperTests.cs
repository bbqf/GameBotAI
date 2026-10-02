#pragma warning disable CA2007, CA1861, CA1859, CA1849
using System;
using System.Threading.Tasks;
using FluentAssertions;
using GameBot.Service.Services.StepThrough;
using Xunit;
using static GameBot.UnitTests.StepThrough.StepThroughServiceRig;

namespace GameBot.UnitTests.StepThrough;

/// <summary>
/// The lease of a step-through (feature 127, FR-012b, R7). A read renews the lease. An expired lease
/// cancels the running step, resumes a queue that the step-through paused, and removes the step-through.
/// </summary>
public sealed class StepThroughLeaseSweeperTests {
  private static readonly TimeSpan Lease = StepThroughService.LeaseDuration;

  [Fact]
  public async Task AStepThroughWithAFreshLeaseIsNotRemoved() {
    var rig = new StepThroughServiceRig();
    var id = await rig.StartAsync(await rig.SeedAsync("one", Cmd(0, "a")));
    rig.Time.Advance(Lease - TimeSpan.FromSeconds(1));

    (await rig.Service.SweepExpiredAsync()).Should().Be(0);

    rig.Service.Get(id).IsSuccess.Should().BeTrue();
  }

  [Fact]
  public async Task AReadRenewsTheLease() {
    var rig = new StepThroughServiceRig();
    var id = await rig.StartAsync(await rig.SeedAsync("one", Cmd(0, "a")));
    rig.Time.Advance(Lease - TimeSpan.FromSeconds(5));
    var renewed = rig.Service.Get(id).Value!.LeaseExpiresAt;
    rig.Time.Advance(TimeSpan.FromSeconds(30));

    (await rig.Service.SweepExpiredAsync()).Should().Be(0);

    renewed.Should().Be(rig.Time.GetUtcNow() - TimeSpan.FromSeconds(30) + Lease);
    rig.Service.Get(id).IsSuccess.Should().BeTrue();
  }

  [Fact]
  public async Task AnExpiredLeaseRemovesTheStepThroughAndFreesTheGameSession() {
    var rig = new StepThroughServiceRig();
    var sequenceId = await rig.SeedAsync("one", Cmd(0, "a"));
    var id = await rig.StartAsync(sequenceId);
    rig.Time.Advance(Lease + TimeSpan.FromSeconds(1));

    (await rig.Service.SweepExpiredAsync()).Should().Be(1);

    rig.Service.Get(id).Error!.Code.Should().Be(StepThroughErrorCodes.StepThroughNotFound);
    (await rig.Service.StartAsync(new StartStepThroughRequest { SequenceId = sequenceId, GameSessionId = GameSessionId })).IsSuccess.Should().BeTrue();
  }

  [Fact]
  public async Task AnExpiredLeaseCancelsTheRunningStep() {
    var rig = new StepThroughServiceRig();
    rig.Wiring.Hold = new TaskCompletionSource<bool>();
    var id = await rig.StartAsync(await rig.SeedAsync("slow", Cmd(0, "a")));
    await rig.Service.RunNextAsync(id);
    var task = rig.Service.RunningTask(id)!;
    task.IsCompleted.Should().BeFalse();
    rig.Time.Advance(Lease + TimeSpan.FromSeconds(1));

    await rig.Service.SweepExpiredAsync();

    task.IsCompleted.Should().BeTrue("the sweep waits for the cancelled step to stop");
    rig.Log.Finalized.Should().ContainSingle().Which.Status.Should().Be("failure");
  }

  [Fact]
  public async Task AnExpiredLeaseResumesAQueueThatTheStepThroughPaused() {
    var rig = new StepThroughServiceRig();
    var queue = rig.AttachQueue();
    var id = await rig.StartAsync(await rig.SeedAsync("one", Cmd(0, "a")));
    rig.Service.PauseQueue(id).IsSuccess.Should().BeTrue();
    queue.Handle.IsPolicyPaused.Should().BeTrue();
    rig.Time.Advance(Lease + TimeSpan.FromSeconds(1));

    await rig.Service.SweepExpiredAsync();

    queue.Handle.IsPolicyPaused.Should().BeFalse();
  }

  [Fact]
  public async Task AnExpiredLeaseLeavesAQueueThatWasAlreadyPaused() {
    var rig = new StepThroughServiceRig();
    var queue = rig.AttachQueue();
    queue.Handle.EnterPolicyPause("failure policy", rig.Time.GetUtcNow());
    var id = await rig.StartAsync(await rig.SeedAsync("one", Cmd(0, "a")));
    rig.Service.PauseQueue(id).IsSuccess.Should().BeTrue();
    rig.Time.Advance(Lease + TimeSpan.FromSeconds(1));

    await rig.Service.SweepExpiredAsync();

    queue.Handle.IsPolicyPaused.Should().BeTrue();
    queue.Handle.PauseReason.Should().Be("failure policy");
  }

  [Fact]
  public async Task EndResumesTheQueueOnlyAfterTheCancelledStepStopped() {
    var rig = new StepThroughServiceRig();
    var queue = rig.AttachQueue();
    rig.Wiring.Hold = new TaskCompletionSource<bool>();
    var id = await rig.StartAsync(await rig.SeedAsync("slow", Cmd(0, "a")));
    rig.Service.PauseQueue(id);
    await rig.Service.RunNextAsync(id);
    var task = rig.Service.RunningTask(id)!;

    await rig.Service.EndAsync(id);

    task.IsCompleted.Should().BeTrue();
    queue.Handle.IsPolicyPaused.Should().BeFalse();
  }

  [Fact]
  public async Task EndOfAnUnknownIdIsIgnoredAndEndTwiceIsSafe() {
    var rig = new StepThroughServiceRig();
    var id = await rig.StartAsync(await rig.SeedAsync("one", Cmd(0, "a")));

    await rig.Service.EndAsync("nope");
    await rig.Service.EndAsync(id);
    await rig.Service.EndAsync(id);

    rig.Service.Get(id).IsSuccess.Should().BeFalse();
  }

  [Fact]
  public async Task OneSweepFailureDoesNotStopTheSweeper() {
    var service = new ThrowingService();
    using var sweeper = new StepThroughLeaseSweeper(service, TimeProvider.System);

    await sweeper.SweepOnceAsync();
    await sweeper.SweepOnceAsync();

    service.Calls.Should().Be(2);
  }

  private sealed class ThrowingService : IStepThroughService {
    public int Calls { get; private set; }

    public Task<int> SweepExpiredAsync() {
      Calls++;
      throw new InvalidOperationException("sweep failed");
    }

    public Task<StepThroughResult<StepThroughStateDto>> StartAsync(StartStepThroughRequest request, System.Threading.CancellationToken ct = default) => throw new NotSupportedException();

    public StepThroughResult<StepThroughStateDto> Get(string id, int? afterSeq = null) => throw new NotSupportedException();

    public Task<StepThroughResult<StepThroughStateDto>> RunNextAsync(string id, System.Threading.CancellationToken ct = default) => throw new NotSupportedException();

    public Task<StepThroughResult<StepThroughStateDto>> SelectAsync(string id, string? path, System.Threading.CancellationToken ct = default) => throw new NotSupportedException();

    public StepThroughResult<CancelOutcome> Cancel(string id) => throw new NotSupportedException();

    public Task<StepThroughResult<StepThroughStateDto>> RestartAsync(string id, System.Threading.CancellationToken ct = default) => throw new NotSupportedException();

    public StepThroughResult<StepThroughStateDto> SetValues(string id, SetValuesRequest request) => throw new NotSupportedException();

    public StepThroughResult<StepThroughStateDto> PauseQueue(string id) => throw new NotSupportedException();

    public Task EndAsync(string id) => throw new NotSupportedException();

    public Task? RunningTask(string id) => null;
  }
}
