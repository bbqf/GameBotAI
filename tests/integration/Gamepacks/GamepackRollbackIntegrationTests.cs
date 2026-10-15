using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using GameBot.Domain.Commands;
using GameBot.Domain.Gamepacks;
using GameBot.UnitTests.Gamepacks;
using Xunit;

#pragma warning disable CA2007, CA1861, CA1707, CA1416

namespace GameBot.IntegrationTests.Gamepacks;

/// <summary>Feature 143 (T037): rollback on real stores (SC-004, FR-011, FR-022).</summary>
public sealed class GamepackRollbackIntegrationTests : IDisposable {
  private readonly GamepackTestKit _kit = new();

  public void Dispose() => _kit.Dispose();

  private async Task InstallAsync(string json) {
    var outcome = await _kit.Service.InstallAsync(json, dryRun: false);
    outcome.Status.Should().Be(GamepackInstallStatus.Ok, string.Join("; ", outcome.Problems.Select(p => p.Message)) + outcome.ErrorMessage);
  }

  private static string V1() => GamepackTestKit.Pack("pns", "1",
    sequences: new[] { GamepackTestKit.Seq("s1", "Daily", "c1"), GamepackTestKit.Seq("s2", "Old") },
    commands: new[] { GamepackTestKit.Cmd("c1", "Tap", imageId: "icon") },
    images: new[] { GamepackTestKit.Img("icon") });

  private static string V2() => GamepackTestKit.Pack("pns", "2",
    sequences: new[] { GamepackTestKit.Seq("s1", "Daily", "c1"), GamepackTestKit.Seq("s3", "Added") },
    commands: new[] { GamepackTestKit.Cmd("c1", "Tap") },
    images: Array.Empty<object>());

  /// <summary>The stored objects and owners as comparable text, without file layout details.</summary>
  private async Task<List<string>> StateAsync() {
    var lines = new List<string>();
    var owners = new GamepackOwnerResolver(_kit.Store.Load());
    foreach (var s in (await _kit.Sequences.ListAsync()).OrderBy(s => s.Name, StringComparer.Ordinal)) {
      lines.Add($"sequence {s.Id} {s.Name} {string.Join(",", s.Steps.Select(x => x.CommandId))} owner={owners.Resolve("sequence", s.Id).ToText()}");
    }

    foreach (var c in (await _kit.Commands.ListAsync()).OrderBy(c => c.Name, StringComparer.Ordinal)) {
      lines.Add($"command {c.Id} {c.Name} {System.Text.Json.JsonSerializer.Serialize(c.Steps)} owner={owners.Resolve("command", c.Id).ToText()}");
    }

    foreach (var id in (await _kit.Images.ListIdsAsync()).OrderBy(i => i, StringComparer.Ordinal)) {
      lines.Add($"image {id} owner={owners.Resolve("image", id).ToText()}");
    }

    return lines;
  }

  [Fact]
  public async Task AnInstallThenARollbackEqualsTheLiveStateCapturedBeforeTheSecondInstall() {
    await InstallAsync(V1());
    // A hand edit before the version 2 install is part of the captured state.
    var cmd = (await _kit.CommandByNameAsync("Tap"))!;
    cmd.Steps.Clear();
    cmd.Steps.Add(new CommandStep { Type = CommandStepType.KeyInput, KeyInput = new KeyInputConfig { Key = "HAND-EDIT" } });
    await _kit.Commands.UpdateAsync(cmd);
    var captured = await StateAsync();

    await InstallAsync(V2());
    // A hand edit after the version 2 install is lost by the rollback.
    var cmdV2 = (await _kit.CommandByNameAsync("Tap"))!;
    cmdV2.Steps.Clear();
    cmdV2.Steps.Add(new CommandStep { Type = CommandStepType.KeyInput, KeyInput = new KeyInputConfig { Key = "LATE-EDIT" } });
    await _kit.Commands.UpdateAsync(cmdV2);

    var outcome = await _kit.Service.RollbackAsync("pns");

    outcome.Status.Should().Be(GamepackRollbackStatus.RolledBack);
    outcome.Version.Should().Be("1");
    (await StateAsync()).Should().Equal(captured);
    _kit.Store.Load().Gamepacks["pns"].Previous.Should().BeNull();
    _kit.Store.ReadPrevious("pns").Should().BeNull();
    (await _kit.SequenceByNameAsync("Added")).Should().BeNull("an object that version 2 added is removed");
  }

  [Fact]
  public async Task ASecondRollbackAndAnUnknownNameAreReported() {
    await InstallAsync(V1());
    (await _kit.Service.RollbackAsync("pns")).Status.Should().Be(GamepackRollbackStatus.NoPreviousVersion);
    await InstallAsync(V2());
    (await _kit.Service.RollbackAsync("PNS")).Status.Should().Be(GamepackRollbackStatus.RolledBack);
    (await _kit.Service.RollbackAsync("pns")).Status.Should().Be(GamepackRollbackStatus.NoPreviousVersion);
    (await _kit.Service.RollbackAsync("nobody")).Status.Should().Be(GamepackRollbackStatus.NotFound);
  }

  [Fact]
  public async Task AFaultAtEveryWriteOfARollbackLeavesTheStateAsBefore() {
    int total;
    using (var counting = new GamepackTestKit()) {
      await counting.Service.InstallAsync(V1(), false);
      await counting.Service.InstallAsync(V2(), false);
      counting.Gate.Arm(0);
      (await counting.Service.RollbackAsync("pns")).Status.Should().Be(GamepackRollbackStatus.RolledBack);
      total = counting.Gate.Calls;
    }

    total.Should().BeGreaterThan(4);
    for (var n = 1; n <= total; n++) {
      using var kit = new GamepackTestKit();
      await kit.Service.InstallAsync(V1(), false);
      await kit.Service.InstallAsync(V2(), false);
      var before = kit.Snapshot();

      kit.Gate.Arm(n);
      var outcome = await kit.Service.RollbackAsync("pns");

      outcome.Status.Should().Be(GamepackRollbackStatus.Failed, "a fault at write call " + n);
      kit.Snapshot().Should().BeEquivalentTo(before, "a fault at write call " + n);
    }
  }

  [Fact]
  public async Task AFailedRestoreInARollbackGivesPartialRestore() {
    await InstallAsync(V1());
    await InstallAsync(V2());
    _kit.Gate.Arm(2);
    _kit.Gate.ThrowOnRestore = true;

    var outcome = await _kit.Service.RollbackAsync("pns");

    outcome.Status.Should().Be(GamepackRollbackStatus.PartialRestore);
  }
}
