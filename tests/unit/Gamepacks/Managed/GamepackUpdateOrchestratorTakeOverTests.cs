using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using GameBot.Domain.Gamepacks;
using GameBot.Domain.Gamepacks.Managed;
using Xunit;

#pragma warning disable CA2007, CA1707, CA1861

namespace GameBot.UnitTests.Gamepacks.Managed;

/// <summary>Feature 149 (T041): the take-over option in the managed update.</summary>
public sealed class GamepackUpdateOrchestratorTakeOverTests {
  private static ManagedUpdateRequest Update(string fileText, bool dryRun = false, bool takeOver = true) => new() {
    Mode = ManagedUpdateMode.Update,
    FileText = fileText,
    DryRun = dryRun,
    TakeOver = takeOver
  };

  private static string UserPack() => GamepackTestKit.Pack("u", "1",
    sequences: new[] { GamepackTestKit.Seq("u-alpha", "alpha", "u-main") },
    commands: new[] { GamepackTestKit.Cmd("u-main", "MainCmd-pns") });

  [Fact]
  public async Task ADryRunPassesTheOptionAndListsTheObjects() {
    using var rig = new ManagedTestRig();
    await rig.Packs.AddUserObjectsAsync(UserPack());
    var before = await rig.StateAsync();

    var report = await rig.RunAsync(Update(ManagedTestRig.Pack("1.0.0", "alpha"), dryRun: true));

    report.Result.Should().Be(ManagedUpdateResults.Planned, ManagedTestRig.Describe(report));
    report.TakenOver.Should().Be(2);
    report.TakeOvers.Select(t => t.Type + ":" + t.Name).Should().BeEquivalentTo("sequence:alpha", "command:MainCmd-pns");
    report.TakeOverBackupId.Should().BeNull();
    (await rig.StateAsync()).Should().Be(before);
    rig.Control.Calls.Should().BeEmpty();
  }

  [Fact]
  public async Task ARealUpdateTakesOverAndReportsTheCountAndTheBackupId() {
    using var rig = new ManagedTestRig();
    await rig.Packs.AddUserObjectsAsync(UserPack());

    var report = await rig.RunAsync(Update(ManagedTestRig.Pack("1.0.0", "alpha")));

    report.Result.Should().Be(ManagedUpdateResults.Done, ManagedTestRig.Describe(report));
    report.TakenOver.Should().Be(2);
    report.TakeOverBackupId.Should().MatchRegex("^to-[0-9]{8}T[0-9]{9}-[0-9a-f]{8}$");
    report.BackupId.Should().BeNull("the queue template backup keeps its meaning");
    report.Created.Should().Be(0);
    report.Updated.Should().Be(0);
  }

  [Fact]
  public async Task TheOptionOffKeepsTheConflict() {
    using var rig = new ManagedTestRig();
    await rig.Packs.AddUserObjectsAsync(UserPack());

    var report = await rig.RunAsync(Update(ManagedTestRig.Pack("1.0.0", "alpha"), takeOver: false));

    report.Result.Should().Be(ManagedUpdateResults.Rejected);
    report.Reason.Should().Be(ManagedUpdateReasons.ValidationFailed);
    report.TakenOver.Should().Be(0);
  }

  [Fact]
  public async Task TheOptionDoesNotChangeWhichQueuesStopOrStart() {
    using var rig = new ManagedTestRig();
    await rig.InstallAsync("1.0.0", "beta");
    await rig.Packs.AddUserObjectsAsync(GamepackTestKit.Pack("u", "1", sequences: new[] { GamepackTestKit.Seq("u-gamma", "gamma") }));
    var affected = await rig.AddTemplateAsync("daily", "beta");
    var running = await rig.AddQueueAsync("Q1", affected, running: true);
    var other = await rig.AddTemplateAsync("other-template");
    await rig.AddQueueAsync("Q2", other, running: true);

    var report = await rig.RunAsync(Update(ManagedTestRig.Pack("2.0.0", "beta", "gamma")));

    report.Result.Should().Be(ManagedUpdateResults.Done, ManagedTestRig.Describe(report));
    rig.Control.StopCalls.Should().Equal(running.Id);
    rig.Control.StartCalls.Should().Equal(running.Id);
  }

  [Fact]
  public async Task AFailureAfterTheInstallGivesTheUserObjectsBackBeforeTheRollback() {
    using var rig = new ManagedTestRig();
    await rig.InstallAsync("1.0.0", "beta");
    await rig.Packs.AddUserObjectsAsync(GamepackTestKit.Pack("u", "1", sequences: new[] { GamepackTestKit.Seq("u-gamma", "gamma") }));
    var gamma = (await rig.Packs.SequenceByNameAsync("gamma"))!;
    var hash = GamepackContentComparer.Hash(gamma);
    var template = await rig.AddTemplateAsync("daily", "beta");
    await rig.AddQueueAsync("Q", template, running: true);
    rig.Templates.FailUpdate = true;

    var report = await rig.RunAsync(Update(ManagedTestRig.Pack("2.0.0", "beta", "gamma")));

    report.Result.Should().Be(ManagedUpdateResults.Failed);
    report.Reason.Should().Be(ManagedUpdateReasons.BookingFailed);
    report.TakeOverBackupId.Should().BeNull("a failed report has no take-over backup id");
    report.Restore.Restored.Should().BeTrue(ManagedTestRig.Describe(report));
    var steps = report.Restore.Steps.Select(s => s.Step).ToList();
    steps.Should().ContainInOrder("takeover_restore", "gamepack_rollback");
    var live = (await rig.Packs.SequenceByNameAsync("gamma"))!;
    live.Id.Should().Be(gamma.Id);
    GamepackContentComparer.Hash(live).Should().Be(hash);
    rig.Packs.OwnerOf(GamepackObjectTypes.Sequence, gamma.Id).IsUser.Should().BeTrue();
  }

  [Fact]
  public async Task ARollbackNeverTakesAnObjectOver() {
    using var rig = new ManagedTestRig();
    await rig.InstallAsync("1.0.0", "alpha");
    await rig.InstallAsync("2.0.0", "alpha", "beta");

    var request = new ManagedUpdateRequest { Mode = ManagedUpdateMode.Rollback, GamepackName = "pns", TakeOver = true };
    var report = await rig.RunAsync(request);

    report.Result.Should().Be(ManagedUpdateResults.Done, ManagedTestRig.Describe(report));
    report.TakenOver.Should().Be(0);
    report.TakeOverBackupId.Should().BeNull();
  }
}
