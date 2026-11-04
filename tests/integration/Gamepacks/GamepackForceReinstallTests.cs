using System;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using GameBot.Domain.Commands;
using GameBot.Domain.Gamepacks;
using GameBot.UnitTests.Gamepacks;
using Xunit;

#pragma warning disable CA2007, CA1861, CA1707, CA1416

namespace GameBot.IntegrationTests.Gamepacks;

/// <summary>A write gate that throws at one operation. A test uses it to make a store write fail (feature 178, FR-006).</summary>
internal sealed class FailingWriteGate : IGamepackWriteGate {
  private readonly string _operation;

  public FailingWriteGate(string operation) => _operation = operation;

  public void BeforeWrite(string operation, string key) {
    if (string.Equals(operation, _operation, StringComparison.Ordinal)) {
      throw new InvalidOperationException("Injected write fault at " + operation);
    }
  }
}

/// <summary>Feature 178 (US1 and US2): a forced install of the same version on real stores.</summary>
public sealed class GamepackForceReinstallTests : IDisposable {
  private readonly GamepackTestKit _kit = new();

  public void Dispose() => _kit.Dispose();

  private static string Pack(string version = "1.0.0") => GamepackTestKit.ReinstallPack(version);

  private async Task InstallAsync(string json) {
    var outcome = await _kit.Service.InstallAsync(json, dryRun: false);
    outcome.Status.Should().Be(GamepackInstallStatus.Ok, string.Join("; ", outcome.Problems.Select(p => p.Message)) + outcome.ErrorMessage);
  }

  private Task<GamepackInstallOutcome> ForceAsync(string json, bool dryRun = false) =>
    _kit.Service.InstallAsync(json, new GamepackInstallOptions(dryRun, Force: true));

  private async Task HandEditCommandAsync(string key) {
    var command = (await _kit.CommandByNameAsync("Tap"))!;
    command.Steps.Clear();
    command.Steps.Add(new CommandStep { Type = CommandStepType.KeyInput, KeyInput = new KeyInputConfig { Key = key } });
    await _kit.Commands.UpdateAsync(command);
  }

  [Fact]
  public async Task AnInstallWithoutForceOfTheSameVersionBehavesAsBefore() {
    await InstallAsync(Pack());

    var outcome = await _kit.Service.InstallAsync(Pack(), new GamepackInstallOptions());

    outcome.Status.Should().Be(GamepackInstallStatus.Ok);
    outcome.Reinstalled.Should().BeFalse();
    outcome.Overwritten.Should().BeNull();
    outcome.Updated.Should().Be(4);
    _kit.Store.ReadReinstall("pns").Should().BeNull();
    _kit.Store.Load().Gamepacks["pns"].Reinstall.Should().BeNull();
  }

  [Fact]
  public async Task AForcedInstallWritesEveryObjectFromTheFileAndKeepsAReinstallPoint() {
    await InstallAsync(Pack());

    var outcome = await ForceAsync(Pack());

    outcome.Status.Should().Be(GamepackInstallStatus.Ok);
    outcome.Reinstalled.Should().BeTrue();
    outcome.Overwritten.Should().BeEmpty("every object equals the file");
    outcome.Updated.Should().Be(4);
    outcome.Created.Should().Be(0);
    _kit.Store.ReadReinstall("pns").Should().NotBeNull();
    _kit.Store.Load().Gamepacks["pns"].Reinstall!.Version.Should().Be("1.0.0");
    _kit.Service.List().Single().HasReinstallPoint.Should().BeTrue();
  }

  [Fact]
  public async Task AHandChangedObjectGetsTheFileContentAndIsListed() {
    await InstallAsync(Pack());
    await HandEditCommandAsync("HAND");

    var outcome = await ForceAsync(Pack());

    outcome.Status.Should().Be(GamepackInstallStatus.Ok);
    outcome.Overwritten.Should().Equal(new OverwriteEntry(GamepackObjectTypes.Command, "Tap"));
    var command = (await _kit.CommandByNameAsync("Tap"))!;
    command.Steps.Should().OnlyContain(s => s.Type == CommandStepType.PrimitiveTap);
  }

  [Fact]
  public async Task AHandDeletedObjectExistsAgainWithTheFileContent() {
    await InstallAsync(Pack());
    var other = (await _kit.SequenceByNameAsync("Other"))!;
    await _kit.Sequences.DeleteAsync(other.Id);
    (await _kit.SequenceByNameAsync("Other")).Should().BeNull();

    var outcome = await ForceAsync(Pack());

    outcome.Status.Should().Be(GamepackInstallStatus.Ok);
    outcome.Created.Should().Be(1);
    outcome.Overwritten.Should().BeEmpty("no data is lost for a deleted object");
    var again = await _kit.SequenceByNameAsync("Other");
    again.Should().NotBeNull();
    _kit.OwnerOf(GamepackObjectTypes.Sequence, again!.Id).Name.Should().Be("pns");
  }

  [Fact]
  public async Task AFailedWriteChangesNoObjectAndLeavesNoReinstallPoint() {
    await InstallAsync(Pack());
    await HandEditCommandAsync("HAND");
    var before = _kit.Snapshot();
    var failing = new GamepackApplier(_kit.Sequences, _kit.Commands, _kit.Images, _kit.Store, new FailingWriteGate(GamepackWriteOperations.Registry),
      takeOverBackups: _kit.TakeOverBackups, templates: _kit.Templates, alternates: _kit.Alternates);
    using var service = new GamepackService(_kit.Sequences, _kit.Commands, _kit.Images, _kit.Store, _kit.Validator, failing);

    var outcome = await service.InstallAsync(Pack(), new GamepackInstallOptions(Force: true));

    outcome.Status.Should().Be(GamepackInstallStatus.Failed);
    outcome.Reinstalled.Should().BeFalse();
    _kit.Snapshot().Should().BeEquivalentTo(before);
    _kit.Store.ReadReinstall("pns").Should().BeNull("no reinstall point hides the earlier state");
    _kit.Store.Load().Gamepacks["pns"].Reinstall.Should().BeNull();
    ((await _kit.CommandByNameAsync("Tap"))!.Steps.Single().Type).Should().Be(CommandStepType.KeyInput);
  }

  [Fact]
  public async Task AFailedWriteAtTheReinstallFileChangesNoObject() {
    await InstallAsync(Pack());
    await HandEditCommandAsync("HAND");
    var before = _kit.Snapshot();
    var failing = new GamepackApplier(_kit.Sequences, _kit.Commands, _kit.Images, _kit.Store, new FailingWriteGate(GamepackWriteOperations.ReinstallFile),
      takeOverBackups: _kit.TakeOverBackups, templates: _kit.Templates, alternates: _kit.Alternates);
    using var service = new GamepackService(_kit.Sequences, _kit.Commands, _kit.Images, _kit.Store, _kit.Validator, failing);

    var outcome = await service.InstallAsync(Pack(), new GamepackInstallOptions(Force: true));

    outcome.Status.Should().Be(GamepackInstallStatus.Failed);
    _kit.Snapshot().Should().BeEquivalentTo(before);
  }

  [Fact]
  public async Task ThePackNeedsNoChangelogFile() {
    var json = GamepackTestKit.Pack("plain", "3", sequences: new[] { GamepackTestKit.Seq("a", "Alone") });
    await InstallAsync(json);

    var outcome = await ForceAsync(json);

    outcome.Status.Should().Be(GamepackInstallStatus.Ok);
    outcome.Reinstalled.Should().BeTrue();
  }

  [Fact]
  public async Task ForceOnAPackThatIsNotInstalledIsANormalFirstInstall() {
    var outcome = await ForceAsync(Pack());

    outcome.Status.Should().Be(GamepackInstallStatus.Ok);
    outcome.Reinstalled.Should().BeFalse();
    outcome.Overwritten.Should().BeNull();
    outcome.Created.Should().Be(4);
    _kit.Store.ReadReinstall("pns").Should().BeNull();
  }

  [Fact]
  public async Task ForceWithADifferentVersionKeepsTheUpdateRulesAndMakesNoReinstallPoint() {
    await InstallAsync(Pack("1.0.0"));

    var update = await ForceAsync(Pack("2.0.0"));

    update.Status.Should().Be(GamepackInstallStatus.Ok);
    update.Reinstalled.Should().BeFalse();
    update.Overwritten.Should().BeNull();
    var record = _kit.Store.Load().Gamepacks["pns"];
    record.Version.Should().Be("2.0.0");
    record.Previous!.Version.Should().Be("1.0.0");
    record.Reinstall.Should().BeNull();

    var downgrade = await ForceAsync(Pack("1.0.0"));

    downgrade.Reinstalled.Should().BeFalse();
    _kit.Store.Load().Gamepacks["pns"].Version.Should().Be("1.0.0");
    _kit.Store.ReadReinstall("pns").Should().BeNull();
  }

  [Fact]
  public async Task AnInstallOfADifferentVersionClearsAStaleReinstallPoint() {
    await InstallAsync(Pack("1.0.0"));
    await ForceAsync(Pack("1.0.0"));
    _kit.Store.ReadReinstall("pns").Should().NotBeNull();

    await InstallAsync(Pack("2.0.0"));

    _kit.Store.ReadReinstall("pns").Should().BeNull();
    _kit.Store.Load().Gamepacks["pns"].Reinstall.Should().BeNull();
  }

  [Fact]
  public async Task ForceWithAnInvalidFileGivesTheNormalInvalidResult() {
    await InstallAsync(Pack());
    var broken = GamepackTestKit.Pack("pns", "1.0.0", sequences: new[] { GamepackTestKit.Seq("s1", "Daily", "missing-command") });

    var outcome = await ForceAsync(broken);

    outcome.Status.Should().Be(GamepackInstallStatus.Invalid);
    outcome.Reinstalled.Should().BeFalse();
  }

  // ---- US2: the dry run ----

  [Fact]
  public async Task AForcedDryRunGivesTheListAndChangesNothing() {
    await InstallAsync(Pack());
    await HandEditCommandAsync("HAND");
    var before = _kit.Snapshot();

    var outcome = await ForceAsync(Pack(), dryRun: true);

    outcome.Status.Should().Be(GamepackInstallStatus.Ok);
    outcome.DryRun.Should().BeTrue();
    outcome.Reinstalled.Should().BeTrue();
    outcome.Overwritten.Should().Equal(new OverwriteEntry(GamepackObjectTypes.Command, "Tap"));
    outcome.Updated.Should().Be(4);
    _kit.Snapshot().Should().BeEquivalentTo(before);
    _kit.Store.ReadReinstall("pns").Should().BeNull();
  }

  [Fact]
  public async Task ADryRunWithoutForceHasNoListAndADryRunOfAnotherVersionHasNoList() {
    await InstallAsync(Pack());

    var plain = await _kit.Service.InstallAsync(Pack(), new GamepackInstallOptions(DryRun: true));
    var other = await ForceAsync(Pack("2.0.0"), dryRun: true);

    plain.Reinstalled.Should().BeFalse();
    plain.Overwritten.Should().BeNull();
    other.Reinstalled.Should().BeFalse();
    other.Overwritten.Should().BeNull();
  }

  [Fact]
  public async Task TwoForcedInstallsKeepOnlyTheNewestPoint() {
    await InstallAsync(Pack());
    await HandEditCommandAsync("FIRST");
    await ForceAsync(Pack());
    await HandEditCommandAsync("SECOND");

    var second = await ForceAsync(Pack());

    second.Overwritten.Should().Equal(new OverwriteEntry(GamepackObjectTypes.Command, "Tap"));
    _kit.Store.ReadReinstall("pns").Should().Contain("SECOND");
    _kit.Store.ReadReinstall("pns").Should().NotContain("FIRST");
  }
}
