using System.IO;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using GameBot.Domain.QueueTemplates;
using System;
using Xunit;

namespace GameBot.UnitTests.QueueTemplates;

/// <summary>
/// Feature 077: the per-entry <see cref="QueueTemplateEntry.Enabled"/> flag defaults to true and
/// round-trips through file persistence, including legacy JSON that predates the field.
/// </summary>
public sealed class QueueTemplateEntryEnabledTests : IDisposable {
  private readonly string _root;

  public QueueTemplateEntryEnabledTests() {
    _root = Path.Combine(Path.GetTempPath(), "GameBotQueueTemplateEnabledTests", Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(_root);
  }

  public void Dispose() {
    try { Directory.Delete(_root, recursive: true); } catch { /* ignore */ }
  }

  [Fact]
  public void NewEntryDefaultsToEnabled() {
    new QueueTemplateEntry().Enabled.Should().BeTrue();
  }

  [Fact]
  public async Task DisabledStateRoundTripsThroughFileRepository() {
    var repo = new FileQueueTemplateRepository(_root);
    var template = new QueueTemplate { Name = "Toggle" };
    template.Entries.Add(new QueueTemplateEntry { SequenceId = "seq-a", Enabled = false });
    template.Entries.Add(new QueueTemplateEntry { SequenceId = "seq-b", Enabled = true });

    var created = await repo.CreateAsync(template).ConfigureAwait(true);
    var loaded = await repo.GetAsync(created.Id).ConfigureAwait(true);

    loaded!.Entries.Should().HaveCount(2);
    loaded.Entries[0].Enabled.Should().BeFalse();
    loaded.Entries[1].Enabled.Should().BeTrue();
  }

  [Fact]
  public async Task LegacyJsonWithoutEnabledKeyDeserializesAsEnabled() {
    // A template file written before the Enabled field existed: no "Enabled" key on the entry.
    var dir = Path.Combine(_root, "queue-templates");
    Directory.CreateDirectory(dir);
    const string legacyId = "legacy1";
    var legacyJson =
      "{\"Id\":\"" + legacyId + "\",\"Name\":\"Legacy\",\"Entries\":[" +
      "{\"SequenceId\":\"seq-a\",\"ScheduleType\":\"OncePerRun\"}]}";
    await File.WriteAllTextAsync(Path.Combine(dir, legacyId + ".json"), legacyJson).ConfigureAwait(true);

    var repo = new FileQueueTemplateRepository(_root);
    var loaded = await repo.GetAsync(legacyId).ConfigureAwait(true);

    loaded.Should().NotBeNull();
    loaded!.Entries.Single().Enabled.Should().BeTrue();
  }
}
