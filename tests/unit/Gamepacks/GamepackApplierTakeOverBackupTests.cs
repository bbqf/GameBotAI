using System.IO;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using GameBot.Domain.Gamepacks;
using Xunit;

#pragma warning disable CA1707, CA2007, CA1861, CA1416

namespace GameBot.UnitTests.Gamepacks;

/// <summary>Feature 149 (T026): the backup before the writes, and the atomic install (SC-004).</summary>
public sealed class GamepackApplierTakeOverBackupTests {
  private static string BackupFolder(GamepackTestKit kit) => Path.Combine(kit.Root, "gamepacks", "takeover-backups");

  private static int BackupFiles(GamepackTestKit kit) =>
    Directory.Exists(BackupFolder(kit)) ? Directory.GetFiles(BackupFolder(kit), "*.json").Length : 0;

  [Fact]
  public async Task TheBackupIsWrittenBeforeTheFirstObjectWrite() {
    using var kit = new GamepackTestKit();
    await kit.AddUserObjectsAsync(GamepackApplierTakeOverTests.UserPack());
    kit.Gate.Arm(0);

    var outcome = await kit.InstallTakeOverAsync(GamepackApplierTakeOverTests.Pack());

    outcome.Status.Should().Be(GamepackInstallStatus.Ok);
    var log = kit.Gate.Log;
    var backupIndex = log.FindIndex(l => l.StartsWith(GamepackWriteOperations.TakeOverBackup + ":", System.StringComparison.Ordinal));
    var firstObjectWrite = log.FindIndex(l => l.StartsWith("update:", System.StringComparison.Ordinal) || l.StartsWith("create:", System.StringComparison.Ordinal));
    backupIndex.Should().BeGreaterThanOrEqualTo(0);
    backupIndex.Should().BeLessThan(firstObjectWrite);
  }

  [Fact]
  public async Task OneBackupHoldsAllReplacedObjects() {
    using var kit = new GamepackTestKit();
    await kit.AddUserObjectsAsync(GamepackApplierTakeOverTests.UserPack());

    var outcome = await kit.InstallTakeOverAsync(GamepackApplierTakeOverTests.Pack());

    BackupFiles(kit).Should().Be(1);
    var backup = kit.TakeOverBackups.Read(outcome.TakeOverBackupId!)!;
    backup.Gamepack.Should().Be("pns");
    backup.Entries.Select(e => e.Type).Should().BeEquivalentTo("sequence", "command", "image");
    backup.Entries.Should().OnlyContain(e => e.PostHash.Length == 64);
  }

  [Fact]
  public async Task NoBackupIsWrittenWhenNothingIsReplaced() {
    using var kit = new GamepackTestKit();

    var outcome = await kit.InstallTakeOverAsync(GamepackApplierTakeOverTests.Pack());

    outcome.Status.Should().Be(GamepackInstallStatus.Ok);
    outcome.TakenOver.Should().Be(0);
    outcome.TakeOverBackupId.Should().BeNull();
    BackupFiles(kit).Should().Be(0);
  }

  [Fact]
  public async Task AFailedBackupWriteGivesFailedAndChangesNothing() {
    using var kit = new GamepackTestKit();
    await kit.AddUserObjectsAsync(GamepackApplierTakeOverTests.UserPack());
    var before = kit.Snapshot();
    kit.Gate.Arm(1);

    var outcome = await kit.InstallTakeOverAsync(GamepackApplierTakeOverTests.Pack());

    outcome.Status.Should().Be(GamepackInstallStatus.Failed);
    outcome.TakeOverBackupId.Should().BeNull();
    kit.Snapshot().Should().BeEquivalentTo(before);
  }

  [Fact]
  public async Task AFaultAtEveryWriteLeavesNoChangeAndNoBackup() {
    int total;
    using (var count = new GamepackTestKit()) {
      await count.AddUserObjectsAsync(GamepackApplierTakeOverTests.UserPack());
      count.Gate.Arm(0);
      (await count.InstallTakeOverAsync(GamepackApplierTakeOverTests.Pack())).Status.Should().Be(GamepackInstallStatus.Ok);
      total = count.Gate.Calls;
    }

    total.Should().BeGreaterThan(4);
    for (var n = 1; n <= total; n++) {
      using var kit = new GamepackTestKit();
      await kit.AddUserObjectsAsync(GamepackApplierTakeOverTests.UserPack());
      var before = kit.Snapshot();
      kit.Gate.Arm(n);

      var outcome = await kit.InstallTakeOverAsync(GamepackApplierTakeOverTests.Pack());

      outcome.Status.Should().Be(GamepackInstallStatus.Failed, "a fault at write call " + n);
      outcome.TakeOverBackupId.Should().BeNull();
      kit.Snapshot().Should().BeEquivalentTo(before, "a fault at write call " + n);
      kit.Store.Load().Gamepacks.Should().BeEmpty();
      BackupFiles(kit).Should().Be(0, "the backup must go with a failed install, call " + n);
    }
  }

  [Fact]
  public async Task AFailedRestoreKeepsTheBackupFile() {
    using var kit = new GamepackTestKit();
    await kit.AddUserObjectsAsync(GamepackApplierTakeOverTests.UserPack());
    kit.Gate.Arm(0);
    (await kit.InstallTakeOverAsync(GamepackApplierTakeOverTests.Pack("1"))).Status.Should().Be(GamepackInstallStatus.Ok);
    var total = kit.Gate.Calls;
    using var second = new GamepackTestKit();
    await second.AddUserObjectsAsync(GamepackApplierTakeOverTests.UserPack());
    second.Gate.Arm(total);
    second.Gate.ThrowOnRestore = true;

    var outcome = await second.InstallTakeOverAsync(GamepackApplierTakeOverTests.Pack("1"));

    outcome.Status.Should().Be(GamepackInstallStatus.PartialRestore);
    BackupFiles(second).Should().Be(1);
  }
}
