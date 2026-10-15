using System.Linq;
using System.Threading.Tasks;
using GameBot.Domain.QueueSettings;
using GameBot.UnitTests.QueueSettings.TestDoubles;
using Xunit;

#pragma warning disable CA2007, CA1707, CA1861

namespace GameBot.UnitTests.QueueSettings;

/// <summary>FR-018: a repository failure gives a partial report and the import can be sent again.</summary>
public sealed class QueueSettingsImporterFailureTests {
  // Write order: command Back (1), sequence Daily (2), template T1 (3), queue Q1 (4).
  private static string Document() => QueueSettingsTestKit.Doc(
    queues: QueueSettingsTestKit.List(QueueSettingsTestKit.QueueJson("Q1", "T1", null, "Daily")),
    templates: QueueSettingsTestKit.List(QueueSettingsTestKit.TemplateJson("T1", "Daily")),
    sequences: QueueSettingsTestKit.List(QueueSettingsTestKit.SequenceJson("Daily", "Back")),
    commands: QueueSettingsTestKit.List(QueueSettingsTestKit.CommandJsonText("Back")));

  [Fact]
  public async Task FailureInTheMiddleGivesPartialReport() {
    using var kit = new QueueSettingsTestKit();
    kit.Probe.FailAtWrite = 3;

    var outcome = await kit.Importer.ImportAsync(Document());

    Assert.Equal(ImportStatus.WriteFailed, outcome.Status);
    var report = outcome.Report!;
    Assert.Equal(new[] { "command:Back", "sequence:Daily" }, report.Items.Select(i => i.Type + ":" + i.Name).ToArray());
    Assert.NotNull(report.Failed);
    Assert.Equal("queueTemplate", report.Failed!.Type);
    Assert.Equal("T1", report.Failed.Name);
    Assert.Equal(ImportOutcomes.Failed, report.Failed.Outcome);
    Assert.False(string.IsNullOrWhiteSpace(report.Failed.Reason));
    Assert.Equal(new[] { "queue:Q1" }, report.NotApplied.Select(i => i.Type + ":" + i.Name).ToArray());
    // The writes before the failure stay. The importer calls no delete and does not undo.
    Assert.NotNull(await kit.Packs.CommandByNameAsync("Back"));
    Assert.NotNull(await kit.Packs.SequenceByNameAsync("Daily"));
    Assert.Null(await kit.TemplateByNameAsync("T1"));
    Assert.Equal(0, kit.Probe.Deletes);
  }

  [Fact]
  public async Task SameDocumentAgainFinishesTheRest() {
    using var kit = new QueueSettingsTestKit();
    kit.Probe.FailAtWrite = 3;
    var first = await kit.Importer.ImportAsync(Document());
    Assert.Equal(ImportStatus.WriteFailed, first.Status);
    kit.Probe.Reset();

    var second = await kit.Importer.ImportAsync(Document());

    Assert.Equal(ImportStatus.Ok, second.Status);
    var report = second.Report!;
    Assert.Null(report.Failed);
    Assert.Empty(report.NotApplied);
    var outcomes = report.Items.ToDictionary(i => i.Type, i => i.Outcome);
    Assert.Equal(ImportOutcomes.Unchanged, outcomes["command"]);
    Assert.Equal(ImportOutcomes.Unchanged, outcomes["sequence"]);
    Assert.Equal(ImportOutcomes.Created, outcomes["queueTemplate"]);
    Assert.Equal(ImportOutcomes.Created, outcomes["queue"]);
    Assert.Single(await kit.Packs.Commands.ListAsync());
    Assert.Single(await kit.Packs.Sequences.ListAsync());
    Assert.Single(await kit.Templates.ListAsync());
    Assert.Single(await kit.Queues.ListAsync());
  }

  [Fact]
  public async Task FailureAtFirstWriteLeavesNothingAppliedAndListsTheRest() {
    using var kit = new QueueSettingsTestKit();
    kit.Probe.FailAtWrite = 1;

    var outcome = await kit.Importer.ImportAsync(Document());

    Assert.Equal(ImportStatus.WriteFailed, outcome.Status);
    Assert.Empty(outcome.Report!.Items);
    Assert.Equal("Back", outcome.Report.Failed!.Name);
    Assert.Equal(3, outcome.Report.NotApplied.Count);
  }
}
