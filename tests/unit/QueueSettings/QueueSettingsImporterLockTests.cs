using System;
using System.Threading.Tasks;
using GameBot.Domain.Gamepacks;
using GameBot.Domain.QueueSettings;
using GameBot.UnitTests.Gamepacks;
using GameBot.UnitTests.QueueSettings.TestDoubles;
using Xunit;

#pragma warning disable CA2007, CA1707, CA1861

namespace GameBot.UnitTests.QueueSettings;

/// <summary>FR-017: an import and a gamepack install run one at a time.</summary>
public sealed class QueueSettingsImporterLockTests {
  private static readonly TimeSpan Wait = TimeSpan.FromMilliseconds(400);

  [Fact]
  public async Task ImportWaitsWhileTheGateIsHeldAndWritesNothingBeforeIt() {
    using var kit = new QueueSettingsTestKit();
    var json = QueueSettingsTestKit.Doc(commands: QueueSettingsTestKit.List(QueueSettingsTestKit.CommandJsonText("Back")));
    Task<ImportOutcome>? import = null;

    await kit.Packs.Service.RunExclusiveAsync(async () => {
      import = kit.Importer.ImportAsync(json);
      await Task.Delay(Wait);
      Assert.False(import.IsCompleted);
      Assert.Equal(0, kit.Probe.Writes);
      return 0;
    });

    var outcome = await import!;
    Assert.Equal(ImportStatus.Ok, outcome.Status);
    Assert.Equal(1, kit.Probe.Writes);
  }

  [Fact]
  public async Task InstallWaitsWhileAnImportHoldsTheGate() {
    using var kit = new QueueSettingsTestKit();
    kit.Probe.BlockAtWrite = 1;
    var json = QueueSettingsTestKit.Doc(commands: QueueSettingsTestKit.List(QueueSettingsTestKit.CommandJsonText("Back")));
    var import = kit.Importer.ImportAsync(json);
    await kit.Probe.Reached.Task.WaitAsync(TimeSpan.FromSeconds(10));

    var pack = GamepackTestKit.Pack("pk", "1.0.0", new[] { GamepackTestKit.Seq("ps", "PackSeq", "pc") }, new[] { GamepackTestKit.Cmd("pc", "PackCmd") });
    var install = kit.Packs.Service.InstallAsync(pack, false);
    await Task.Delay(Wait);
    Assert.False(install.IsCompleted);
    Assert.False(import.IsCompleted);

    kit.Probe.Release();
    var outcome = await import;
    var installed = await install;

    Assert.Equal(ImportStatus.Ok, outcome.Status);
    Assert.Equal(GamepackInstallStatus.Ok, installed.Status);
  }
}
