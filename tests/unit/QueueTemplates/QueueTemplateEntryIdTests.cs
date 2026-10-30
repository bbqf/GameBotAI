using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using GameBot.Domain.QueueTemplates;
using Xunit;

namespace GameBot.UnitTests.QueueTemplates;

/// <summary>
/// Feature 171: the stable <see cref="QueueTemplateEntry.EntryId"/> and the computed
/// <see cref="QueueTemplate.NeedsUpgrade"/> mark. An empty ID is omitted from the stored JSON, so an old
/// file keeps its text until the operator upgrades it.
/// </summary>
public sealed class QueueTemplateEntryIdTests : IDisposable {
  private readonly string _root;

  public QueueTemplateEntryIdTests() {
    _root = Path.Combine(Path.GetTempPath(), "GameBotQueueTemplateEntryIdTests", Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(_root);
  }

  public void Dispose() {
    try { Directory.Delete(_root, recursive: true); } catch { /* ignore */ }
  }

  [Fact]
  public void NewEntryHasEmptyId() {
    new QueueTemplateEntry().EntryId.Should().BeEmpty();
  }

  [Fact]
  public async Task EmptyIdIsOmittedFromStoredJson() {
    var repo = new FileQueueTemplateRepository(_root);
    var template = new QueueTemplate { Name = "NoIds" };
    template.Entries.Add(new QueueTemplateEntry { SequenceId = "seq-a" });

    var created = await repo.CreateAsync(template).ConfigureAwait(true);

    var json = await File.ReadAllTextAsync(Path.Combine(_root, "queue-templates", created.Id + ".json")).ConfigureAwait(true);
    json.Should().NotContain("entryId");
  }

  [Fact]
  public async Task IdRoundTripsThroughFileRepository() {
    var repo = new FileQueueTemplateRepository(_root);
    var template = new QueueTemplate { Name = "WithIds" };
    template.Entries.Add(new QueueTemplateEntry { SequenceId = "seq-a", EntryId = "aaa111" });
    template.Entries.Add(new QueueTemplateEntry { SequenceId = "seq-a", EntryId = "bbb222" });

    var created = await repo.CreateAsync(template).ConfigureAwait(true);
    var loaded = await repo.GetAsync(created.Id).ConfigureAwait(true);

    loaded!.Entries.Select(e => e.EntryId).Should().Equal("aaa111", "bbb222");
  }

  [Fact]
  public async Task LegacyJsonWithoutIdLoadsWithEmptyIdAndNeedsUpgrade() {
    var dir = Path.Combine(_root, "queue-templates");
    Directory.CreateDirectory(dir);
    const string legacyId = "legacy1";
    var legacyJson =
      "{\"Id\":\"" + legacyId + "\",\"Name\":\"Legacy\",\"Entries\":[" +
      "{\"SequenceId\":\"seq-a\",\"ScheduleType\":\"OncePerRun\"}]}";
    await File.WriteAllTextAsync(Path.Combine(dir, legacyId + ".json"), legacyJson).ConfigureAwait(true);

    var repo = new FileQueueTemplateRepository(_root);
    var loaded = await repo.GetAsync(legacyId).ConfigureAwait(true);

    loaded!.Entries.Single().EntryId.Should().BeEmpty();
    loaded.NeedsUpgrade.Should().BeTrue();
  }

  [Fact]
  public void EmptyTemplateDoesNotNeedUpgrade() {
    new QueueTemplate { Name = "Empty" }.NeedsUpgrade.Should().BeFalse();
  }

  [Fact]
  public void TemplateWithAllIdsDoesNotNeedUpgrade() {
    var template = new QueueTemplate { Name = "Done" };
    template.Entries.Add(new QueueTemplateEntry { SequenceId = "seq-a", EntryId = "aaa111" });

    template.NeedsUpgrade.Should().BeFalse();
  }

  [Fact]
  public void TemplateWithOneMissingIdNeedsUpgrade() {
    var template = new QueueTemplate { Name = "Mixed" };
    template.Entries.Add(new QueueTemplateEntry { SequenceId = "seq-a", EntryId = "aaa111" });
    template.Entries.Add(new QueueTemplateEntry { SequenceId = "seq-b" });

    template.NeedsUpgrade.Should().BeTrue();
  }
}
