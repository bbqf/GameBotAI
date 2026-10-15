using System.Linq;
using System.Threading.Tasks;
using GameBot.Domain.Gamepacks;
using GameBot.Domain.Queues;
using GameBot.Domain.QueueSettings;
using GameBot.UnitTests.Gamepacks;
using GameBot.UnitTests.QueueSettings.TestDoubles;
using Xunit;

#pragma warning disable CA2007, CA1707, CA1861

namespace GameBot.UnitTests.QueueSettings;

public sealed class QueueSettingsImporterTests {
  private static string FullDocument() => QueueSettingsTestKit.Doc(
    queues: QueueSettingsTestKit.List(QueueSettingsTestKit.QueueJson("Q1", "T1", "Game One", "Daily", "Daily")),
    templates: QueueSettingsTestKit.List(QueueSettingsTestKit.TemplateJson("T1", "Daily")),
    sequences: QueueSettingsTestKit.List(QueueSettingsTestKit.SequenceJson("Daily", "Back")),
    commands: QueueSettingsTestKit.List(QueueSettingsTestKit.CommandJsonText("Back")));

  private static async Task<ImportReport> ImportOkAsync(QueueSettingsTestKit kit, string json) {
    var outcome = await kit.Importer.ImportAsync(json);
    Assert.Equal(ImportStatus.Ok, outcome.Status);
    return outcome.Report!;
  }

  [Fact]
  public async Task CreatesAllObjectsWithNewIdsAndRewrittenLinks() {
    using var kit = new QueueSettingsTestKit();
    var game = await kit.AddGameAsync("Game One");

    var report = await ImportOkAsync(kit, FullDocument());

    Assert.Equal(4, report.Summary.Created);
    Assert.Empty(report.UnresolvedReferences);
    var command = (await kit.Packs.CommandByNameAsync("Back"))!;
    var sequence = (await kit.Packs.SequenceByNameAsync("Daily"))!;
    var template = (await kit.TemplateByNameAsync("T1"))!;
    var queue = (await kit.QueueByNameAsync("Q1"))!;
    Assert.Equal(command.Id, sequence.Steps.Single().CommandId);
    Assert.Equal(sequence.Id, template.Entries.Single().SequenceId);
    Assert.Equal(template.Id, queue.LinkedTemplateId);
    Assert.Equal(game.Id, queue.LinkedGameId);
    Assert.Equal(new[] { sequence.Id, sequence.Id }, kit.Runtime.GetEntries(queue.Id).Select(e => e.SequenceId).ToArray());
  }

  [Fact]
  public async Task ChangedObjectIsUpdatedAndKeepsItsId() {
    using var kit = new QueueSettingsTestKit();
    await kit.AddGameAsync("Game One");
    await ImportOkAsync(kit, FullDocument());
    var before = (await kit.Packs.SequenceByNameAsync("Daily"))!;
    var changed = FullDocument().Replace("\"Version\":1", "\"Version\":2", System.StringComparison.Ordinal);

    var report = await ImportOkAsync(kit, changed);

    Assert.Equal(1, report.Summary.Updated);
    Assert.Equal(3, report.Summary.Unchanged);
    var after = (await kit.Packs.SequenceByNameAsync("Daily"))!;
    Assert.Equal(before.Id, after.Id);
    Assert.Equal(2, after.Version);
    Assert.Equal(before.CreatedAt, after.CreatedAt);
  }

  [Fact]
  public async Task SecondImportIsUnchangedAndWritesNothing() {
    using var kit = new QueueSettingsTestKit();
    await kit.AddGameAsync("Game One");
    await ImportOkAsync(kit, FullDocument());
    kit.Probe.Reset();

    var report = await ImportOkAsync(kit, FullDocument());

    Assert.Equal(4, report.Summary.Unchanged);
    Assert.Equal(0, report.Summary.Created + report.Summary.Updated + report.Summary.Skipped);
    Assert.Equal(0, kit.Probe.Writes);
    Assert.Equal(0, kit.Probe.Deletes);
  }

  [Fact]
  public async Task ExportedDocumentImportsAsUnchanged() {
    using var kit = new QueueSettingsTestKit();
    var command = await kit.AddCommandAsync("Back");
    var sequence = await kit.AddSequenceAsync("Daily", command.Id);
    var template = await kit.AddTemplateAsync("T1", sequence.Id);
    await kit.AddQueueAsync("Q1", template.Id, null, sequence.Id);
    var json = await kit.ExportJsonAsync();
    kit.Probe.Reset();

    var report = await ImportOkAsync(kit, json);

    Assert.Equal(4, report.Summary.Unchanged);
    Assert.Equal(0, kit.Probe.Writes);
  }

  [Fact]
  public async Task OwnedNameWithFullContentIsSkippedAndNothingChanges() {
    using var kit = new QueueSettingsTestKit();
    var pack = GamepackTestKit.Pack("pk", "1.0.0", new[] { GamepackTestKit.Seq("ps", "PackSeq", "pc") }, new[] { GamepackTestKit.Cmd("pc", "PackCmd") });
    Assert.Equal(GamepackInstallStatus.Ok, (await kit.Packs.Service.InstallAsync(pack, false)).Status);
    var before = kit.Packs.Snapshot();
    kit.Probe.Reset();
    var json = QueueSettingsTestKit.Doc(
      sequences: QueueSettingsTestKit.List(QueueSettingsTestKit.SequenceJson("packseq", "PackCmd")),
      commands: QueueSettingsTestKit.List(QueueSettingsTestKit.CommandJsonText("PackCmd")));

    var report = await ImportOkAsync(kit, json);

    Assert.Equal(2, report.Summary.Skipped);
    Assert.All(report.Items, i => Assert.Equal(ImportReasons.OwnedByGamepack, i.Reason));
    Assert.Equal(0, kit.Probe.Writes);
    Assert.Equal(before, kit.Packs.Snapshot());
    var owner = kit.Packs.OwnerOf(GamepackObjectTypes.Sequence, (await kit.Packs.SequenceByNameAsync("PackSeq"))!.Id);
    Assert.Equal("pk", owner.Name);
  }

  [Fact]
  public async Task ImportNeverDeletes() {
    using var kit = new QueueSettingsTestKit();
    var extra = await kit.AddSequenceAsync("NotInDocument");
    await kit.AddQueueAsync("OtherQueue");

    await ImportOkAsync(kit, FullDocument());

    Assert.Equal(0, kit.Probe.Deletes);
    Assert.NotNull(await kit.Packs.Sequences.GetAsync(extra.Id));
    Assert.NotNull(await kit.QueueByNameAsync("OtherQueue"));
  }

  [Fact]
  public async Task RunningQueueUpdateSetsRestartRequiredAndDoesNotStopIt() {
    using var kit = new QueueSettingsTestKit();
    var queue = await kit.AddQueueAsync("Q1");
    kit.Runtime.SetStatus(queue.Id, QueueExecutionStatus.Running);
    var json = QueueSettingsTestKit.Doc(queues: QueueSettingsTestKit.List(
      "{\"name\":\"Q1\",\"emulatorSerial\":\"emulator-5554\",\"cycleExecution\":true}"));

    var report = await ImportOkAsync(kit, json);

    var item = Assert.Single(report.Items);
    Assert.Equal(ImportOutcomes.Updated, item.Outcome);
    Assert.True(item.RestartRequired);
    Assert.Equal(QueueExecutionStatus.Running, kit.Runtime.GetStatus(queue.Id));
    Assert.True((await kit.QueueByNameAsync("Q1"))!.CycleExecution);
  }

  [Fact]
  public async Task StoppedQueueUpdateNeedsNoRestart() {
    using var kit = new QueueSettingsTestKit();
    await kit.AddQueueAsync("Q1");
    var json = QueueSettingsTestKit.Doc(queues: QueueSettingsTestKit.List(
      "{\"name\":\"Q1\",\"emulatorSerial\":\"emulator-5554\",\"pauseWhenIdle\":true}"));

    var report = await ImportOkAsync(kit, json);

    Assert.False(Assert.Single(report.Items).RestartRequired);
  }

  [Fact]
  public async Task MachineSpecificFieldsAreImportedAsGivenWithoutReportEntry() {
    using var kit = new QueueSettingsTestKit();
    var json = QueueSettingsTestKit.Doc(queues: QueueSettingsTestKit.List(
      "{\"name\":\"Q1\",\"emulatorSerial\":\"emulator-9999\",\"emulatorInstanceName\":\"LD-9\",\"emulatorInstanceIndex\":9}"));

    var report = await ImportOkAsync(kit, json);

    Assert.Empty(report.UnresolvedReferences);
    Assert.Single(report.Items);
    var queue = (await kit.QueueByNameAsync("Q1"))!;
    Assert.Equal("emulator-9999", queue.EmulatorSerial);
    Assert.Equal("LD-9", queue.EmulatorInstanceName);
    Assert.Equal(9, queue.EmulatorInstanceIndex);
  }
}
