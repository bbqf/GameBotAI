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

/// <summary>Feature 178 (US3): the rollback of a forced install gives back the objects from before it.</summary>
public sealed class GamepackForceRollbackTests : IDisposable {
  private readonly GamepackTestKit _kit = new();

  public void Dispose() => _kit.Dispose();

  private static string Pack(string version) => GamepackTestKit.ReinstallPack(version);

  private async Task InstallAsync(string json, bool force = false) {
    var outcome = await _kit.Service.InstallAsync(json, new GamepackInstallOptions(Force: force));
    outcome.Status.Should().Be(GamepackInstallStatus.Ok, string.Join("; ", outcome.Problems.Select(p => p.Message)) + outcome.ErrorMessage);
  }

  private async Task HandEditCommandAsync(string key) {
    var command = (await _kit.CommandByNameAsync("Tap"))!;
    command.Steps.Clear();
    command.Steps.Add(new CommandStep { Type = CommandStepType.KeyInput, KeyInput = new KeyInputConfig { Key = key } });
    await _kit.Commands.UpdateAsync(command);
  }

  private async Task<string> TapKeyAsync() => (await _kit.CommandByNameAsync("Tap"))!.Steps.Single().KeyInput!.Key;

  [Fact]
  public async Task ARollbackAfterAForcedInstallGivesBackTheHandMadeChange() {
    await InstallAsync(Pack("1"));
    await InstallAsync(Pack("2"));
    var previousFile = _kit.Store.ReadPrevious("pns");
    await HandEditCommandAsync("HAND");
    await InstallAsync(Pack("2"), force: true);
    (await _kit.CommandByNameAsync("Tap"))!.Steps.Single().Type.Should().Be(CommandStepType.PrimitiveTap);

    var outcome = await _kit.Service.RollbackAsync("pns");

    outcome.Status.Should().Be(GamepackRollbackStatus.RolledBack);
    outcome.Version.Should().Be("2");
    (await TapKeyAsync()).Should().Be("HAND");
    var record = _kit.Store.Load().Gamepacks["pns"];
    record.Version.Should().Be("2");
    record.Previous!.Version.Should().Be("1");
    _kit.Store.ReadPrevious("pns").Should().Be(previousFile);
    record.Reinstall.Should().BeNull();
    _kit.Store.ReadReinstall("pns").Should().BeNull();
  }

  [Fact]
  public async Task ASecondRollbackGoesToThePreviousVersion() {
    await InstallAsync(Pack("1"));
    await InstallAsync(Pack("2"));
    await InstallAsync(Pack("2"), force: true);
    (await _kit.Service.RollbackAsync("pns")).Version.Should().Be("2");

    var second = await _kit.Service.RollbackAsync("pns");

    second.Status.Should().Be(GamepackRollbackStatus.RolledBack);
    second.Version.Should().Be("1");
    _kit.Store.Load().Gamepacks["pns"].Previous.Should().BeNull();
  }

  [Fact]
  public async Task ARollbackWithOnlyAReinstallPointWorks() {
    await InstallAsync(Pack("1"));
    await HandEditCommandAsync("HAND");
    await InstallAsync(Pack("1"), force: true);

    var outcome = await _kit.Service.RollbackAsync("pns");

    outcome.Status.Should().Be(GamepackRollbackStatus.RolledBack);
    (await TapKeyAsync()).Should().Be("HAND");
    (await _kit.Service.RollbackAsync("pns")).Status.Should().Be(GamepackRollbackStatus.NoPreviousVersion);
  }

  [Fact]
  public async Task ARollbackWithNeitherPointIsReportedAsBefore() {
    await InstallAsync(Pack("1"));

    (await _kit.Service.RollbackAsync("pns")).Status.Should().Be(GamepackRollbackStatus.NoPreviousVersion);
  }

  [Fact]
  public async Task AFaultInTheRollbackKeepsTheReinstallPoint() {
    await InstallAsync(Pack("1"));
    await HandEditCommandAsync("HAND");
    await InstallAsync(Pack("1"), force: true);
    var before = _kit.Snapshot();
    var failing = new GamepackApplier(_kit.Sequences, _kit.Commands, _kit.Images, _kit.Store, new FailingWriteGate(GamepackWriteOperations.Registry),
      takeOverBackups: _kit.TakeOverBackups, templates: _kit.Templates, alternates: _kit.Alternates);
    using var service = new GamepackService(_kit.Sequences, _kit.Commands, _kit.Images, _kit.Store, _kit.Validator, failing);

    var outcome = await service.RollbackAsync("pns");

    outcome.Status.Should().Be(GamepackRollbackStatus.Failed);
    _kit.Snapshot().Should().BeEquivalentTo(before);
    _kit.Store.ReadReinstall("pns").Should().NotBeNull();
  }

  [Fact]
  public async Task RemoveDeletesTheReinstallFile() {
    await InstallAsync(Pack("1"));
    await InstallAsync(Pack("1"), force: true);
    _kit.Store.ReadReinstall("pns").Should().NotBeNull();

    var outcome = await _kit.Service.RemoveAsync("pns");

    outcome.Status.Should().Be(GamepackRemoveStatus.Removed);
    _kit.Store.ReadReinstall("pns").Should().BeNull();
  }

  [Fact]
  public async Task RemoveInstalledDeletesTheReinstallFile() {
    await InstallAsync(Pack("1"));
    await InstallAsync(Pack("1"), force: true);

    var failed = await _kit.Service.RemoveInstalledAsync("pns", fileText: null);

    failed.Should().BeEmpty();
    _kit.Store.ReadReinstall("pns").Should().BeNull();
  }

  [Fact]
  public async Task TheListShowsTheReinstallPoint() {
    await InstallAsync(Pack("1"));
    _kit.Service.List().Single().HasReinstallPoint.Should().BeFalse();

    await InstallAsync(Pack("1"), force: true);
    _kit.Service.List().Single().HasReinstallPoint.Should().BeTrue();

    await _kit.Service.RollbackAsync("pns");
    _kit.Service.List().Single().HasReinstallPoint.Should().BeFalse();
  }
}
