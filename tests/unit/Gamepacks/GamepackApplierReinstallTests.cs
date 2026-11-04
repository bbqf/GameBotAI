using System;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using GameBot.Domain.Commands;
using GameBot.Domain.Gamepacks;
using Xunit;

#pragma warning disable CA1707, CA2007, CA1861, CA1416

namespace GameBot.UnitTests.Gamepacks;

/// <summary>Feature 178 (T030, T031): the applier keeps, captures, and clears the reinstall point, and restores it on a fault.</summary>
public sealed class GamepackApplierReinstallTests : IDisposable {
  private readonly GamepackTestKit _kit = new();

  public void Dispose() => _kit.Dispose();

  private async Task InstallAsync(string json) {
    var outcome = await _kit.Service.InstallAsync(json, dryRun: false);
    outcome.Status.Should().Be(GamepackInstallStatus.Ok);
  }

  private async Task<GamepackApplyOutcome> ApplyAsync(ReinstallPointHandling handling, string? json = null) {
    var parsed = GamepackFileSerializer.Parse(json ?? GamepackOverwriteCompareTests.Pack());
    var check = await _kit.Validator.ValidateAsync(parsed.File!, parsed.Problems, default, reinstall: true);
    check.IsValid.Should().BeTrue();
    return await _kit.Applier.ApplyAsync(check.Plan!, GamepackPreviousHandling.Keep, default, handling);
  }

  private async Task HandEditAsync(string key) {
    var command = (await _kit.CommandByNameAsync("Tap"))!;
    command.Steps.Clear();
    command.Steps.Add(new CommandStep { Type = CommandStepType.KeyInput, KeyInput = new KeyInputConfig { Key = key } });
    await _kit.Commands.UpdateAsync(command);
  }

  [Fact]
  public async Task KeepLeavesTheReinstallPointAsItIs() {
    await InstallAsync(GamepackOverwriteCompareTests.Pack());

    (await ApplyAsync(ReinstallPointHandling.Keep)).Status.Should().Be(GamepackApplyStatus.Applied);

    _kit.Store.ReadReinstall("pns").Should().BeNull();
    _kit.Store.Load().Gamepacks["pns"].Reinstall.Should().BeNull();
  }

  [Fact]
  public async Task CaptureWritesTheLiveContentAndTheRecordAndKeepsThePreviousVersion() {
    await InstallAsync(GamepackOverwriteCompareTests.Pack("1"));
    await InstallAsync(GamepackOverwriteCompareTests.Pack("2"));
    var previousBefore = _kit.Store.ReadPrevious("pns");
    await HandEditAsync("LIVE-EDIT");

    var applied = await ApplyAsync(ReinstallPointHandling.Capture, GamepackOverwriteCompareTests.Pack("2"));

    applied.Status.Should().Be(GamepackApplyStatus.Applied);
    _kit.Store.ReadReinstall("pns").Should().Contain("LIVE-EDIT");
    var record = _kit.Store.Load().Gamepacks["pns"];
    record.Reinstall.Should().NotBeNull();
    record.Reinstall!.Version.Should().Be("2");
    record.Version.Should().Be("2");
    _kit.Store.ReadPrevious("pns").Should().Be(previousBefore);
    record.Previous!.Version.Should().Be("1");
  }

  [Fact]
  public async Task ASecondCaptureReplacesTheFirstPoint() {
    await InstallAsync(GamepackOverwriteCompareTests.Pack());
    await HandEditAsync("FIRST");
    await ApplyAsync(ReinstallPointHandling.Capture);
    await HandEditAsync("SECOND");

    await ApplyAsync(ReinstallPointHandling.Capture);

    var text = _kit.Store.ReadReinstall("pns");
    text.Should().Contain("SECOND");
    text.Should().NotContain("FIRST");
  }

  [Fact]
  public async Task ClearDeletesTheFileAndTheRecordMember() {
    await InstallAsync(GamepackOverwriteCompareTests.Pack());
    await ApplyAsync(ReinstallPointHandling.Capture);
    _kit.Store.ReadReinstall("pns").Should().NotBeNull();

    (await ApplyAsync(ReinstallPointHandling.Clear)).Status.Should().Be(GamepackApplyStatus.Applied);

    _kit.Store.ReadReinstall("pns").Should().BeNull();
    _kit.Store.Load().Gamepacks["pns"].Reinstall.Should().BeNull();
  }

  [Fact]
  public async Task KeepAfterACaptureKeepsTheFileAndTheRecordMember() {
    await InstallAsync(GamepackOverwriteCompareTests.Pack());
    await ApplyAsync(ReinstallPointHandling.Capture);

    await ApplyAsync(ReinstallPointHandling.Keep);

    _kit.Store.ReadReinstall("pns").Should().NotBeNull();
    _kit.Store.Load().Gamepacks["pns"].Reinstall.Should().NotBeNull();
  }

  [Fact]
  public async Task AFaultAtEveryWriteOfAForcedApplyLeavesTheWholeStateAsBefore() {
    // T031: a fault at any write, also at the write of the reinstall file, restores the old reinstall file.
    int total;
    using (var counting = new GamepackTestKit()) {
      await counting.Service.InstallAsync(GamepackOverwriteCompareTests.Pack(), dryRun: false);
      counting.Gate.Arm(0);
      var parsed = GamepackFileSerializer.Parse(GamepackOverwriteCompareTests.Pack());
      var check = await counting.Validator.ValidateAsync(parsed.File!, parsed.Problems, default, reinstall: true);
      (await counting.Applier.ApplyAsync(check.Plan!, GamepackPreviousHandling.Keep, default, ReinstallPointHandling.Capture)).Status.Should().Be(GamepackApplyStatus.Applied);
      total = counting.Gate.Calls;
      counting.Gate.Log.Should().Contain(l => l.StartsWith(GamepackWriteOperations.ReinstallFile + ":", StringComparison.Ordinal));
    }

    total.Should().BeGreaterThan(4);
    for (var n = 1; n <= total; n++) {
      using var kit = new GamepackTestKit();
      await kit.Service.InstallAsync(GamepackOverwriteCompareTests.Pack(), dryRun: false);
      var first = GamepackFileSerializer.Parse(GamepackOverwriteCompareTests.Pack());
      var firstCheck = await kit.Validator.ValidateAsync(first.File!, first.Problems, default, reinstall: true);
      await kit.Applier.ApplyAsync(firstCheck.Plan!, GamepackPreviousHandling.Keep, default, ReinstallPointHandling.Capture);
      var before = kit.Snapshot();

      kit.Gate.Arm(n);
      var parsed = GamepackFileSerializer.Parse(GamepackOverwriteCompareTests.Pack());
      var check = await kit.Validator.ValidateAsync(parsed.File!, parsed.Problems, default, reinstall: true);
      var applied = await kit.Applier.ApplyAsync(check.Plan!, GamepackPreviousHandling.Keep, default, ReinstallPointHandling.Capture);

      applied.Status.Should().Be(GamepackApplyStatus.Failed, "a fault at write call " + n);
      kit.Snapshot().Should().BeEquivalentTo(before, "a fault at write call " + n);
    }
  }

  [Fact]
  public async Task AFaultAfterTheFirstPointRestoresTheOldFileText() {
    await InstallAsync(GamepackOverwriteCompareTests.Pack());
    await HandEditAsync("OLD-POINT");
    await ApplyAsync(ReinstallPointHandling.Capture);
    var oldText = _kit.Store.ReadReinstall("pns");
    await HandEditAsync("NEWER");
    var parsed = GamepackFileSerializer.Parse(GamepackOverwriteCompareTests.Pack());
    var check = await _kit.Validator.ValidateAsync(parsed.File!, parsed.Problems, default, reinstall: true);

    // Fault at the registry write. It comes after the object writes and after the write of the reinstall file.
    var writes = check.Plan!.Writes.Count + check.Plan.Removals.Count;
    _kit.Gate.Arm(writes + 2);
    var applied = await _kit.Applier.ApplyAsync(check.Plan, GamepackPreviousHandling.Keep, default, ReinstallPointHandling.Capture);

    applied.Status.Should().Be(GamepackApplyStatus.Failed);
    _kit.Gate.Log.Should().Contain(GamepackWriteOperations.ReinstallFile + ":pns");
    _kit.Store.ReadReinstall("pns").Should().Be(oldText);
  }
}
