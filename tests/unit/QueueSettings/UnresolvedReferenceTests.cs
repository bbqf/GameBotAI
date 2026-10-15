using System.Linq;
using System.Threading.Tasks;
using GameBot.Domain.QueueSettings;
using GameBot.UnitTests.QueueSettings.TestDoubles;
using Xunit;

#pragma warning disable CA2007, CA1707, CA1861

namespace GameBot.UnitTests.QueueSettings;

/// <summary>FR-008: a missing target does not fail the import. The report lists it (research R6).</summary>
public sealed class UnresolvedReferenceTests {
  private static async Task<ImportReport> ImportOkAsync(QueueSettingsTestKit kit, string json) {
    var outcome = await kit.Importer.ImportAsync(json);
    Assert.Equal(ImportStatus.Ok, outcome.Status);
    return outcome.Report!;
  }

  [Fact]
  public async Task NoMissingTargetGivesEmptyList() {
    using var kit = new QueueSettingsTestKit();
    var json = QueueSettingsTestKit.Doc(
      sequences: QueueSettingsTestKit.List(QueueSettingsTestKit.SequenceJson("S", "C")),
      commands: QueueSettingsTestKit.List(QueueSettingsTestKit.CommandJsonText("C")));

    var report = await ImportOkAsync(kit, json);

    Assert.Empty(report.UnresolvedReferences);
  }

  [Fact]
  public async Task TemplateEntryWithMissingSequenceIsSkippedAndListed() {
    using var kit = new QueueSettingsTestKit();
    var references = QueueSettingsTestKit.List("{\"type\":\"sequence\",\"name\":\"Pit Mining\",\"gamepack\":\"pns\",\"version\":\"1.4.0\",\"usedBy\":\"queueTemplate:Daily\"}");
    var json = QueueSettingsTestKit.Doc(
      templates: QueueSettingsTestKit.List(QueueSettingsTestKit.TemplateJson("Daily", "Pit Mining")),
      references: references);

    var report = await ImportOkAsync(kit, json);

    var missing = Assert.Single(report.UnresolvedReferences);
    Assert.Equal("sequence", missing.Type);
    Assert.Equal("Pit Mining", missing.Name);
    Assert.Equal("queueTemplate:Daily", missing.UsedBy);
    Assert.Equal("pns", missing.Gamepack);
    Assert.Equal("1.4.0", missing.Version);
    Assert.Empty((await kit.TemplateByNameAsync("Daily"))!.Entries);
  }

  [Fact]
  public async Task QueueEntryWithMissingSequenceIsSkippedAndListed() {
    using var kit = new QueueSettingsTestKit();
    var json = QueueSettingsTestKit.Doc(queues: QueueSettingsTestKit.List(QueueSettingsTestKit.QueueJson("Q1", null, null, "Gone")));

    var report = await ImportOkAsync(kit, json);

    var missing = Assert.Single(report.UnresolvedReferences);
    Assert.Equal(("sequence", "Gone", "queue:Q1"), (missing.Type, missing.Name, missing.UsedBy));
    var queue = (await kit.QueueByNameAsync("Q1"))!;
    Assert.Empty(kit.Runtime.GetEntries(queue.Id));
  }

  [Fact]
  public async Task SequenceWithMissingCommandIsWrittenAndListed() {
    using var kit = new QueueSettingsTestKit();
    var json = QueueSettingsTestKit.Doc(sequences: QueueSettingsTestKit.List(QueueSettingsTestKit.SequenceJson("S", "Nowhere")));

    var report = await ImportOkAsync(kit, json);

    var missing = Assert.Single(report.UnresolvedReferences);
    Assert.Equal(("command", "Nowhere", "sequence:S"), (missing.Type, missing.Name, missing.UsedBy));
    Assert.Equal(1, report.Summary.Created);
  }

  [Fact]
  public async Task CommandWithMissingCommandAndMissingImageIsListed() {
    using var kit = new QueueSettingsTestKit();
    var command = "{\"name\":\"C\",\"steps\":[{\"type\":\"Command\",\"order\":0,\"targetId\":\"Nowhere\"},"
      + "{\"type\":\"PrimitiveTap\",\"order\":1,\"primitiveTap\":{\"detectionTarget\":{\"referenceImageId\":\"no-image\",\"confidence\":0.8,\"offsetX\":0,\"offsetY\":0,\"selectionStrategy\":\"HighestConfidence\"}}}]}";
    var json = QueueSettingsTestKit.Doc(commands: QueueSettingsTestKit.List(command));

    var report = await ImportOkAsync(kit, json);

    Assert.Contains(report.UnresolvedReferences, r => r.Type == "command" && r.Name == "Nowhere" && r.UsedBy == "command:C");
    Assert.Contains(report.UnresolvedReferences, r => r.Type == "image" && r.Name == "no-image" && r.UsedBy == "command:C");
  }

  [Fact]
  public async Task MissingLinkedTemplateAndGameAreListedAndLinksStayEmpty() {
    using var kit = new QueueSettingsTestKit();
    var json = QueueSettingsTestKit.Doc(queues: QueueSettingsTestKit.List(QueueSettingsTestKit.QueueJson("Q1", "NoTemplate", "NoGame")));

    var report = await ImportOkAsync(kit, json);

    Assert.Contains(report.UnresolvedReferences, r => r.Type == "queueTemplate" && r.Name == "NoTemplate" && r.UsedBy == "queue:Q1");
    Assert.Contains(report.UnresolvedReferences, r => r.Type == "game" && r.Name == "NoGame" && r.UsedBy == "queue:Q1");
    var queue = (await kit.QueueByNameAsync("Q1"))!;
    Assert.Null(queue.LinkedTemplateId);
    Assert.Null(queue.LinkedGameId);
    Assert.Equal(1, report.Summary.Created);
  }

  [Fact]
  public async Task MissingGamepackOwnedSequenceInQueueIsListedWithGamepackText() {
    using var kit = new QueueSettingsTestKit();
    var references = QueueSettingsTestKit.List("{\"type\":\"sequence\",\"name\":\"Help\",\"gamepack\":\"pns\",\"version\":\"2.0.0\",\"usedBy\":\"queue:Q1\"}");
    var json = QueueSettingsTestKit.Doc(queues: QueueSettingsTestKit.List(QueueSettingsTestKit.QueueJson("Q1", null, null, "Help")), references: references);

    var report = await ImportOkAsync(kit, json);

    var missing = Assert.Single(report.UnresolvedReferences);
    Assert.Equal(("pns", "2.0.0"), (missing.Gamepack, missing.Version));
  }

  [Fact]
  public async Task SecondImportOfSameDocumentWithMissingTargetIsUnchanged() {
    using var kit = new QueueSettingsTestKit();
    var json = QueueSettingsTestKit.Doc(
      queues: QueueSettingsTestKit.List(QueueSettingsTestKit.QueueJson("Q1", "NoTemplate", null, "Gone")),
      templates: QueueSettingsTestKit.List(QueueSettingsTestKit.TemplateJson("Daily", "Gone")),
      sequences: QueueSettingsTestKit.List(QueueSettingsTestKit.SequenceJson("S", "Nowhere")));
    await ImportOkAsync(kit, json);
    kit.Probe.Reset();

    var report = await ImportOkAsync(kit, json);

    Assert.Equal(3, report.Items.Count);
    Assert.All(report.Items, i => Assert.Equal(ImportOutcomes.Unchanged, i.Outcome));
    Assert.Equal(0, kit.Probe.Writes);
    Assert.NotEmpty(report.UnresolvedReferences.Where(r => r.Name == "Gone"));
  }
}
