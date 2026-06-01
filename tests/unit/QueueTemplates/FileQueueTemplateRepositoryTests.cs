using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using GameBot.Domain.QueueTemplates;
using Xunit;

namespace GameBot.UnitTests.QueueTemplates;

public sealed class FileQueueTemplateRepositoryTests : IDisposable {
  private readonly string _root;

  public FileQueueTemplateRepositoryTests() {
    _root = Path.Combine(Path.GetTempPath(), "GameBotQueueTemplateTests", Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(_root);
  }

  public void Dispose() {
    try { Directory.Delete(_root, recursive: true); } catch { /* ignore */ }
  }

  private FileQueueTemplateRepository NewRepo() => new FileQueueTemplateRepository(_root);

  private static QueueTemplate TemplateWith(string name, params string[] sequenceIds) {
    var t = new QueueTemplate { Name = name };
    foreach (var sid in sequenceIds) t.Entries.Add(new QueueTemplateEntry { SequenceId = sid });
    return t;
  }

  [Fact]
  public async Task CreateAssignsIdAndPersistsEntriesInOrder() {
    var repo = NewRepo();
    var created = await repo.CreateAsync(TemplateWith("Daily Farm", "seq-a", "seq-b", "seq-a")).ConfigureAwait(true);

    created.Id.Should().NotBeNullOrWhiteSpace();
    created.CreatedAt.Should().NotBeNull();

    var loaded = await repo.GetAsync(created.Id).ConfigureAwait(true);
    loaded.Should().NotBeNull();
    loaded!.Name.Should().Be("Daily Farm");
    loaded.Entries.Select(e => e.SequenceId).Should().ContainInOrder("seq-a", "seq-b", "seq-a");
  }

  [Fact]
  public async Task PersistedFileContainsEntries() {
    var repo = NewRepo();
    var created = await repo.CreateAsync(TemplateWith("Daily Farm", "seq-x")).ConfigureAwait(true);

    var json = await File.ReadAllTextAsync(Path.Combine(_root, "queue-templates", created.Id + ".json")).ConfigureAwait(true);
    json.Should().Contain("seq-x");
    json.Should().Contain("Daily Farm");
  }

  [Fact]
  public async Task EmptyTemplateIsAllowed() {
    var repo = NewRepo();
    var created = await repo.CreateAsync(TemplateWith("Empty")).ConfigureAwait(true);

    var loaded = await repo.GetAsync(created.Id).ConfigureAwait(true);
    loaded!.Entries.Should().BeEmpty();
  }

  [Fact]
  public async Task FindByNameIsCaseInsensitive() {
    var repo = NewRepo();
    await repo.CreateAsync(TemplateWith("Daily Farm", "seq-a")).ConfigureAwait(true);

    var found = await repo.FindByNameAsync("daily farm").ConfigureAwait(true);
    found.Should().NotBeNull();
    found!.Name.Should().Be("Daily Farm");

    (await repo.FindByNameAsync("nope").ConfigureAwait(true)).Should().BeNull();
  }

  [Fact]
  public async Task UpdateReplacesEntriesAndBumpsUpdatedAt() {
    var repo = NewRepo();
    var created = await repo.CreateAsync(TemplateWith("Daily Farm", "seq-a")).ConfigureAwait(true);
    var originalUpdated = created.UpdatedAt;

    created.Entries.Clear();
    created.Entries.Add(new QueueTemplateEntry { SequenceId = "seq-b" });
    created.Entries.Add(new QueueTemplateEntry { SequenceId = "seq-c" });
    await Task.Delay(5).ConfigureAwait(true);
    var updated = await repo.UpdateAsync(created).ConfigureAwait(true);

    updated.UpdatedAt.Should().BeOnOrAfter(originalUpdated!.Value);
    var loaded = await repo.GetAsync(created.Id).ConfigureAwait(true);
    loaded!.Entries.Select(e => e.SequenceId).Should().ContainInOrder("seq-b", "seq-c");
  }

  [Fact]
  public async Task ListReturnsAllTemplates() {
    var repo = NewRepo();
    await repo.CreateAsync(TemplateWith("A", "seq-a")).ConfigureAwait(true);
    await repo.CreateAsync(TemplateWith("B", "seq-b")).ConfigureAwait(true);

    var all = await repo.ListAsync().ConfigureAwait(true);
    all.Select(t => t.Name).Should().Contain("A").And.Contain("B");
    all.Should().HaveCount(2);
  }

  [Fact]
  public async Task DeleteRemovesTemplate() {
    var repo = NewRepo();
    var created = await repo.CreateAsync(TemplateWith("A", "seq-a")).ConfigureAwait(true);

    (await repo.DeleteAsync(created.Id).ConfigureAwait(true)).Should().BeTrue();
    (await repo.GetAsync(created.Id).ConfigureAwait(true)).Should().BeNull();
    (await repo.DeleteAsync(created.Id).ConfigureAwait(true)).Should().BeFalse();
  }

  [Fact]
  public async Task GetWithUnsafeIdReturnsNull() {
    var repo = NewRepo();
    (await repo.GetAsync("../escape").ConfigureAwait(true)).Should().BeNull();
  }

  [Fact]
  public async Task CreateWithoutNameThrows() {
    var repo = NewRepo();
    var act = async () => await repo.CreateAsync(TemplateWith("")).ConfigureAwait(true);
    await act.Should().ThrowAsync<InvalidOperationException>().ConfigureAwait(true);
  }
}
