using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using GameBot.Domain.Gamepacks;
using Xunit;

#pragma warning disable CA1707, CA2007, CA1861, CA1416, CA1859

namespace GameBot.UnitTests.Gamepacks;

/// <summary>A registry store that wraps a real store. It can fail a write or a previous-file delete.</summary>
internal sealed class FaultyRegistryStore : IGamepackRegistryStore {
  private readonly IGamepackRegistryStore _inner;

  public FaultyRegistryStore(IGamepackRegistryStore inner) => _inner = inner;

  public bool FailUpdate { get; set; }

  public bool FailDeletePrevious { get; set; }

  public GamepackRegistry Load() => _inner.Load();

  public bool RegistryExists => _inner.RegistryExists;

  public void DeleteRegistry() => _inner.DeleteRegistry();

  public void Save(GamepackRegistry registry) => _inner.Save(registry);

  public void Update(Action<GamepackRegistry> change) {
    if (FailUpdate) {
      throw new IOException("Injected registry write fault.");
    }

    _inner.Update(change);
  }

  public string? ReadPrevious(string name) => _inner.ReadPrevious(name);

  public void WritePrevious(string name, string text) => _inner.WritePrevious(name, text);

  public void DeletePrevious(string name) {
    if (FailDeletePrevious) {
      throw new IOException("Injected delete fault.");
    }

    _inner.DeletePrevious(name);
  }
}

/// <summary>Feature 156: the removal of the record of an installed gamepack.</summary>
public sealed class GamepackServiceRemoveTests {
  private static string V1() => GamepackTestKit.Pack("pns", "1.0.0",
    sequences: new[] { GamepackTestKit.Seq("s1", "Seq One") },
    commands: new[] { GamepackTestKit.Cmd("c1", "Cmd One") },
    images: new[] { GamepackTestKit.Img("img1") });

  private static string V2() => GamepackTestKit.Pack("pns", "1.1.0",
    sequences: new[] { GamepackTestKit.Seq("s1", "Seq One") },
    commands: new[] { GamepackTestKit.Cmd("c1", "Cmd One") },
    images: new[] { GamepackTestKit.Img("img1") });

  private static string Other() => GamepackTestKit.Pack("other", "2.0.0",
    sequences: new[] { GamepackTestKit.Seq("o1", "Other Seq") });

  private static async Task InstallAsync(GamepackTestKit kit, string json) =>
    (await kit.Service.InstallAsync(json, dryRun: false)).Status.Should().Be(GamepackInstallStatus.Ok);

  private static GamepackService ServiceWith(GamepackTestKit kit, IGamepackRegistryStore store) =>
    new(kit.Sequences, kit.Commands, kit.Images, store, kit.Validator, kit.Applier);

  [Fact]
  public async Task ARemoveDeletesTheRecordAndAllOwnerEntries() {
    using var kit = new GamepackTestKit();
    await InstallAsync(kit, V1());

    var outcome = await kit.Service.RemoveAsync("PNS");

    outcome.Status.Should().Be(GamepackRemoveStatus.Removed);
    outcome.Name.Should().Be("pns");
    outcome.Version.Should().Be("1.0.0");
    outcome.OwnersRemoved.Should().Be(3);
    outcome.HadPreviousVersion.Should().BeFalse();
    var registry = kit.Store.Load();
    registry.Gamepacks.Should().BeEmpty();
    registry.Owners.Should().BeEmpty();
  }

  [Fact]
  public async Task AnOwnerEntryOfAMissingItemIsRemovedToo() {
    using var kit = new GamepackTestKit();
    await InstallAsync(kit, V1());
    var sequenceId = (await kit.SequenceByNameAsync("Seq One"))!.Id;
    await kit.Sequences.DeleteAsync(sequenceId);

    var outcome = await kit.Service.RemoveAsync("pns");

    outcome.Status.Should().Be(GamepackRemoveStatus.Removed);
    kit.Store.Load().Owners.Should().BeEmpty();
  }

  [Fact]
  public async Task ARemoveKeepsTheOwnerEntriesOfOtherGamepacks() {
    using var kit = new GamepackTestKit();
    await InstallAsync(kit, V1());
    await InstallAsync(kit, Other());

    (await kit.Service.RemoveAsync("pns")).Status.Should().Be(GamepackRemoveStatus.Removed);

    var registry = kit.Store.Load();
    registry.Gamepacks.Keys.Should().BeEquivalentTo("other");
    registry.Owners.Values.Should().OnlyContain(o => o.Gamepack == "other");
    registry.Owners.Should().HaveCount(1);
  }

  [Fact]
  public async Task ARemoveDeletesTheKeptPreviousFile() {
    using var kit = new GamepackTestKit();
    await InstallAsync(kit, V1());
    await InstallAsync(kit, V2());
    kit.Store.ReadPrevious("pns").Should().NotBeNull();

    var outcome = await kit.Service.RemoveAsync("pns");

    outcome.Status.Should().Be(GamepackRemoveStatus.Removed);
    outcome.HadPreviousVersion.Should().BeTrue();
    kit.Store.ReadPrevious("pns").Should().BeNull();
  }

  [Fact]
  public async Task ARemoveKeepsTakeOverBackupFiles() {
    using var kit = new GamepackTestKit();
    await kit.AddUserObjectsAsync(GamepackApplierTakeOverTests.UserPack());
    var install = await kit.InstallTakeOverAsync(GamepackApplierTakeOverTests.Pack());
    var backup = Path.Combine(kit.Root, "gamepacks", "takeover-backups", install.TakeOverBackupId + ".json");
    File.Exists(backup).Should().BeTrue();

    (await kit.Service.RemoveAsync("pns")).Status.Should().Be(GamepackRemoveStatus.Removed);

    File.Exists(backup).Should().BeTrue();
  }

  [Fact]
  public async Task ARemoveDoesNotTouchTheContentStores() {
    using var kit = new GamepackTestKit();
    await InstallAsync(kit, V1());
    var sequences = (await kit.Sequences.ListAsync()).Select(s => s.Id).OrderBy(i => i, StringComparer.Ordinal).ToList();
    var commands = (await kit.Commands.ListAsync()).Select(c => c.Id).OrderBy(i => i, StringComparer.Ordinal).ToList();
    var images = (await kit.Images.ListIdsAsync()).OrderBy(i => i, StringComparer.Ordinal).ToList();

    await kit.Service.RemoveAsync("pns");

    (await kit.Sequences.ListAsync()).Select(s => s.Id).OrderBy(i => i, StringComparer.Ordinal).Should().Equal(sequences);
    (await kit.Commands.ListAsync()).Select(c => c.Id).OrderBy(i => i, StringComparer.Ordinal).Should().Equal(commands);
    (await kit.Images.ListIdsAsync()).OrderBy(i => i, StringComparer.Ordinal).Should().Equal(images);
    sequences.Should().NotBeEmpty();
  }

  [Theory]
  [InlineData("nobody")]
  [InlineData("..\\bad")]
  public async Task AnUnknownOrInvalidNameGivesNotFoundAndNoWrite(string name) {
    using var kit = new GamepackTestKit();
    await InstallAsync(kit, V1());
    var before = kit.Snapshot();

    var outcome = await kit.Service.RemoveAsync(name);

    outcome.Status.Should().Be(GamepackRemoveStatus.NotFound);
    kit.Snapshot().Should().BeEquivalentTo(before);
  }

  [Fact]
  public async Task AFailedRegistryWriteGivesFailedAndKeepsEverything() {
    using var kit = new GamepackTestKit();
    await InstallAsync(kit, V1());
    await InstallAsync(kit, V2());
    var before = kit.Snapshot();
    var faulty = new FaultyRegistryStore(kit.Store) { FailUpdate = true };
    using var service = ServiceWith(kit, faulty);

    var outcome = await service.RemoveAsync("pns");

    outcome.Status.Should().Be(GamepackRemoveStatus.Failed);
    outcome.ErrorMessage.Should().NotBeNullOrEmpty();
    kit.Snapshot().Should().BeEquivalentTo(before);
    kit.Store.ReadPrevious("pns").Should().NotBeNull();
  }

  [Fact]
  public async Task AFailedDeleteOfThePreviousFileStillGivesRemoved() {
    using var kit = new GamepackTestKit();
    await InstallAsync(kit, V1());
    await InstallAsync(kit, V2());
    var faulty = new FaultyRegistryStore(kit.Store) { FailDeletePrevious = true };
    using var service = ServiceWith(kit, faulty);

    var outcome = await service.RemoveAsync("pns");

    outcome.Status.Should().Be(GamepackRemoveStatus.Removed);
    kit.Store.Load().Gamepacks.Should().BeEmpty();
  }

  [Fact]
  public async Task ARemoveWaitsForTheGate() {
    using var kit = new GamepackTestKit();
    await InstallAsync(kit, V1());
    var release = new TaskCompletionSource();
    var started = new TaskCompletionSource();
    var holder = kit.Service.RunExclusiveAsync(async () => {
      started.SetResult();
      await release.Task;
      return 0;
    });
    await started.Task;

    var remove = kit.Service.RemoveAsync("pns");
    await Task.Delay(200);
    remove.IsCompleted.Should().BeFalse();
    kit.Store.Load().Gamepacks.Should().NotBeEmpty();

    release.SetResult();
    await holder;
    (await remove).Status.Should().Be(GamepackRemoveStatus.Removed);
  }
}
