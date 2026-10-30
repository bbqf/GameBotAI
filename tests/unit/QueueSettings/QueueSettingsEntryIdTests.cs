using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using GameBot.Domain.Queues;
using GameBot.Domain.QueueSettings;
using GameBot.UnitTests.QueueSettings.TestDoubles;
using Xunit;

#pragma warning disable CA2007, CA1707, CA1861

namespace GameBot.UnitTests.QueueSettings;

/// <summary>
/// Feature 171: the settings export carries the entry ID of a template entry, and the import keeps it. A new
/// template from an import gets IDs. A stored template that needs an upgrade gets none from an import.
/// </summary>
public sealed class QueueSettingsEntryIdTests {
  private static string TemplateWithIds(string name, params (string Sequence, string? EntryId)[] entries) =>
    JsonSerializer.Serialize(new {
      name,
      entries = entries.Select(e => new { entryId = e.EntryId, sequence = e.Sequence, enabled = true, scheduleType = "OncePerRun" }).ToArray()
    });

  private static string Document(string template) => QueueSettingsTestKit.Doc(
    templates: QueueSettingsTestKit.List(template),
    sequences: QueueSettingsTestKit.List(QueueSettingsTestKit.SequenceJson("Daily", "Back")),
    commands: QueueSettingsTestKit.List(QueueSettingsTestKit.CommandJsonText("Back")));

  [Fact]
  public async Task ExportWritesTheEntryIdOfAnUpgradedTemplate() {
    using var kit = new QueueSettingsTestKit();
    var command = await kit.AddCommandAsync("Back");
    var sequence = await kit.AddSequenceAsync("Daily", command.Id);
    var template = new GameBot.Domain.QueueTemplates.QueueTemplate { Id = "t1", Name = "T1" };
    template.Entries.Add(new GameBot.Domain.QueueTemplates.QueueTemplateEntry { SequenceId = sequence.Id, EntryId = "entry-one" });
    await kit.Templates.CreateAsync(template);

    var document = await kit.ExportAsync();

    Assert.Equal("entry-one", document.QueueTemplates.Single().Entries.Single().EntryId);
    Assert.Contains("\"entryId\": \"entry-one\"", await kit.ExportJsonAsync(), System.StringComparison.Ordinal);
  }

  [Fact]
  public async Task ExportLeavesOutTheEntryIdOfATemplateThatNeedsAnUpgrade() {
    using var kit = new QueueSettingsTestKit();
    var command = await kit.AddCommandAsync("Back");
    var sequence = await kit.AddSequenceAsync("Daily", command.Id);
    await kit.AddTemplateAsync("Old", sequence.Id);

    var document = await kit.ExportAsync();

    Assert.Null(document.QueueTemplates.Single().Entries.Single().EntryId);
    Assert.DoesNotContain("entryId", await kit.ExportJsonAsync(), System.StringComparison.Ordinal);
  }

  [Fact]
  public async Task ImportKeepsTheEntryIdOfTheDocument() {
    using var kit = new QueueSettingsTestKit();

    var outcome = await kit.Importer.ImportAsync(Document(TemplateWithIds("T1", ("Daily", "keep-me"))));

    Assert.Equal(ImportStatus.Ok, outcome.Status);
    var template = (await kit.TemplateByNameAsync("T1"))!;
    Assert.Equal("keep-me", template.Entries.Single().EntryId);
    Assert.False(template.NeedsUpgrade);
  }

  [Fact]
  public async Task ImportOfANewTemplateWithNoIdsGivesIds() {
    using var kit = new QueueSettingsTestKit();

    var outcome = await kit.Importer.ImportAsync(Document(TemplateWithIds("T1", ("Daily", null), ("Daily", null))));

    Assert.Equal(ImportStatus.Ok, outcome.Status);
    var template = (await kit.TemplateByNameAsync("T1"))!;
    Assert.False(template.NeedsUpgrade);
    Assert.Equal(2, template.Entries.Select(e => e.EntryId).Distinct().Count());
  }

  [Fact]
  public async Task ImportOverAStoredTemplateThatNeedsAnUpgradeCreatesNoId() {
    using var kit = new QueueSettingsTestKit();
    var command = await kit.AddCommandAsync("Back");
    var sequence = await kit.AddSequenceAsync("Daily", command.Id);
    await kit.AddTemplateAsync("T1", sequence.Id);

    var changed = Document(JsonSerializer.Serialize(new {
      name = "T1",
      entries = new[] { new { sequence = "Daily", enabled = false, scheduleType = "OncePerRun" } }
    }));
    var outcome = await kit.Importer.ImportAsync(changed);

    Assert.Equal(ImportStatus.Ok, outcome.Status);
    var template = (await kit.TemplateByNameAsync("T1"))!;
    Assert.True(template.NeedsUpgrade);
    Assert.False(template.Entries.Single().Enabled);
  }

  [Fact]
  public async Task ImportOverAnUpgradedTemplateKeepsTheIdAtTheSamePositionAndSequence() {
    using var kit = new QueueSettingsTestKit();
    var command = await kit.AddCommandAsync("Back");
    var sequence = await kit.AddSequenceAsync("Daily", command.Id);
    var stored = new GameBot.Domain.QueueTemplates.QueueTemplate { Id = "t1", Name = "T1" };
    stored.Entries.Add(new GameBot.Domain.QueueTemplates.QueueTemplateEntry { SequenceId = sequence.Id, EntryId = "stays" });
    await kit.Templates.CreateAsync(stored);

    var changed = Document(JsonSerializer.Serialize(new {
      name = "T1",
      entries = new[] { new { sequence = "Daily", enabled = false, scheduleType = "OncePerRun" } }
    }));
    var outcome = await kit.Importer.ImportAsync(changed);

    Assert.Equal(ImportStatus.Ok, outcome.Status);
    Assert.Equal("stays", (await kit.TemplateByNameAsync("T1"))!.Entries.Single().EntryId);
  }

  [Fact]
  public async Task ImportOfAnIdOnlyChangeReportsNoChange() {
    using var kit = new QueueSettingsTestKit();
    var command = await kit.AddCommandAsync("Back");
    var sequence = await kit.AddSequenceAsync("Daily", command.Id);
    await kit.AddTemplateAsync("T1", sequence.Id);

    var outcome = await kit.Importer.ImportAsync(Document(TemplateWithIds("T1", ("Daily", "new-id"))));

    Assert.Equal(ImportStatus.Ok, outcome.Status);
    var item = outcome.Report!.Items.Single(i => i.Type == SettingsObjectTypes.QueueTemplate);
    Assert.Equal(ImportOutcomes.Unchanged, item.Outcome);
  }

  [Fact]
  public async Task ImportRejectsTheSameEntryIdTwiceInOneTemplate() {
    using var kit = new QueueSettingsTestKit();

    var outcome = await kit.Importer.ImportAsync(Document(TemplateWithIds("T1", ("Daily", "same"), ("Daily", "SAME"))));

    Assert.Equal(ImportStatus.Invalid, outcome.Status);
    Assert.Contains(outcome.Errors!, e => e.Contains("entryId", System.StringComparison.Ordinal));
  }
}
