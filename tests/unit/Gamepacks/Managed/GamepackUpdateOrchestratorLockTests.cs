using System;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using GameBot.Domain.Gamepacks.Managed;
using Xunit;

#pragma warning disable CA2007, CA1707, CA1861

namespace GameBot.UnitTests.Gamepacks.Managed;

/// <summary>Feature 148 (T023, FR-015): one managed call at a time on the whole instance.</summary>
public sealed class GamepackUpdateOrchestratorLockTests {
  private static readonly TimeSpan Wait = TimeSpan.FromSeconds(10);

  private static async Task<(ManagedTestRig Rig, TaskCompletionSource Release, TaskCompletionSource Entered, Task<ManagedUpdateReport> First)> StartHeldCallAsync() {
    var rig = new ManagedTestRig();
    await rig.InstallAsync("1.0.0", "alpha");
    var template = await rig.AddTemplateAsync("daily", "alpha");
    await rig.AddQueueAsync("Q", template, running: true);
    var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    rig.Control.OnStop = async _ => {
      entered.TrySetResult();
      await release.Task;
    };
    var first = rig.RunAsync(ManagedTestRig.Update(ManagedTestRig.Pack("2.0.0", "alpha", "gamma")));
    await entered.Task.WaitAsync(Wait);
    return (rig, release, entered, first);
  }

  [Fact]
  public async Task ASecondCallForAnotherGamepackGetsUpdateInProgressAndStopsNothing() {
    var (rig, release, _, first) = await StartHeldCallAsync();
    using (rig) {
      var second = await rig.RunAsync(ManagedTestRig.Update(ManagedTestRig.PackNamed("other", "1.0.0", "zeta")));

      second.Result.Should().Be(ManagedUpdateResults.Rejected);
      second.Reason.Should().Be(ManagedUpdateReasons.UpdateInProgress);
      rig.Control.StopCalls.Should().HaveCount(1);
      rig.Service.List().Select(g => g.Name).Should().Equal("pns");

      release.SetResult();
      (await first).Result.Should().Be(ManagedUpdateResults.Done);
    }
  }

  [Fact]
  public async Task TheLockIsReleasedAfterASuccessfulCall() {
    var (rig, release, _, first) = await StartHeldCallAsync();
    using (rig) {
      release.SetResult();
      await first;

      var again = await rig.RunAsync(ManagedTestRig.Update(ManagedTestRig.PackNamed("other", "1.0.0", "zeta")));

      again.Result.Should().Be(ManagedUpdateResults.Done, ManagedTestRig.Describe(again));
      rig.Orchestrator.IsBusy.Should().BeFalse();
    }
  }

  [Fact]
  public async Task TheLockIsReleasedAfterAFailedCall() {
    using var rig = new ManagedTestRig();
    await rig.InstallAsync("1.0.0", "alpha");
    rig.Packs.Gate.Arm(1);

    var failed = await rig.RunAsync(ManagedTestRig.Update(ManagedTestRig.Pack("2.0.0", "alpha", "gamma")));
    rig.Packs.Gate.Arm(0);
    var again = await rig.RunAsync(ManagedTestRig.Update(ManagedTestRig.Pack("2.0.0", "alpha", "gamma")));

    failed.Result.Should().Be(ManagedUpdateResults.Failed);
    again.Result.Should().Be(ManagedUpdateResults.Done, ManagedTestRig.Describe(again));
  }

  [Fact]
  public async Task ADryRunTakesNoLockAndReportsTheRunningCallAsAProblem() {
    var (rig, release, _, first) = await StartHeldCallAsync();
    using (rig) {
      var dry = await rig.RunAsync(ManagedTestRig.Update(ManagedTestRig.Pack("3.0.0", "alpha"), dryRun: true));

      dry.Result.Should().Be(ManagedUpdateResults.Rejected);
      dry.Reason.Should().Be(ManagedUpdateReasons.UpdateInProgress);
      dry.Problems.Should().Contain(p => p.Code == ManagedUpdateReasons.UpdateInProgress);

      release.SetResult();
      await first;
      rig.Orchestrator.IsBusy.Should().BeFalse();
    }
  }

  [Fact]
  public async Task ADryRunWhenIdleLeavesTheInstanceFreeForTheNextCall() {
    using var rig = new ManagedTestRig();
    await rig.InstallAsync("1.0.0", "alpha");

    var dry = await rig.RunAsync(ManagedTestRig.Update(ManagedTestRig.Pack("2.0.0", "alpha"), dryRun: true));
    var real = await rig.RunAsync(ManagedTestRig.Update(ManagedTestRig.Pack("2.0.0", "alpha")));

    dry.Result.Should().Be(ManagedUpdateResults.Planned, ManagedTestRig.Describe(dry));
    real.Result.Should().Be(ManagedUpdateResults.Done, ManagedTestRig.Describe(real));
  }
}
