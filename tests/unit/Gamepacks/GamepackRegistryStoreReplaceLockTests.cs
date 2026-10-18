using System;
using System.IO;
using System.Threading.Tasks;
using FluentAssertions;
using GameBot.Domain.Gamepacks;
using GameBot.UnitTests.Utils;
using Xunit;

#pragma warning disable CA1707, CA2007, CA1861, CA1416, CA2000, CA1849, CA2000, CA1031

namespace GameBot.UnitTests.Gamepacks;

/// <summary>Issue #333 (T013, T021, FR-008a): registry and previous-file writes while another handle holds the old file.</summary>
public sealed class GamepackRegistryStoreReplaceLockTests {
  private static GamepackRegistryStore Arrange() {
    var store = new GamepackRegistryStore(FileHold.NewFolder());
    store.Save(new GamepackRegistry());
    store.WritePrevious("pns", "old");
    return store;
  }

  [Fact]
  public async Task AShortLockOnTheRegistryDoesNotStopSaveOrUpdate() {
    if (!OperatingSystem.IsWindows()) {
      return;
    }

    var store = Arrange();
    var hold = FileHold.Open(store.RegistryFilePath!);
    var release = FileHold.ReleaseAfter(hold, 300);
    store.Save(new GamepackRegistry());
    await release;

    hold = FileHold.Open(store.RegistryFilePath!);
    release = FileHold.ReleaseAfter(hold, 300);
    store.Update(r => r.Owners["sequence:a"] = new OwnerEntry { Gamepack = "pns", Version = "1", Id = "a" });
    await release;

    store.Load().Owners.Should().ContainKey("sequence:a");
  }

  [Fact]
  public async Task AShortLockOnThePreviousFileDoesNotStopWritePrevious() {
    if (!OperatingSystem.IsWindows()) {
      return;
    }

    var store = Arrange();
    var hold = FileHold.Open(store.PreviousFilePath("pns")!);
    var release = FileHold.ReleaseAfter(hold, 300);
    store.WritePrevious("pns", "new");
    await release;

    store.ReadPrevious("pns").Should().Be("new");
  }

  [Fact]
  public void ALongLockOnTheRegistryGivesTheFileErrorForSaveAndUpdate() {
    if (!OperatingSystem.IsWindows()) {
      return;
    }

    var store = Arrange();
    using var hold = FileHold.Open(store.RegistryFilePath!);

    var save = () => store.Save(new GamepackRegistry());
    var update = () => store.Update(_ => { });

    save.Should().Throw<IOException>().Which.Message.Should().StartWith("Cannot replace file '" + store.RegistryFilePath + "' for registry '");
    update.Should().Throw<IOException>().Which.Message.Should().StartWith("Cannot replace file '" + store.RegistryFilePath + "' for registry '");
  }

  [Fact]
  public void ALongLockOnThePreviousFileGivesTheFileErrorWithThePackName() {
    if (!OperatingSystem.IsWindows()) {
      return;
    }

    var store = Arrange();
    using var hold = FileHold.Open(store.PreviousFilePath("pns")!);

    var act = () => store.WritePrevious("pns", "new");

    act.Should().Throw<IOException>().Which.Message
      .Should().StartWith("Cannot replace file '" + store.PreviousFilePath("pns") + "' for previous 'pns': ");
  }
}
