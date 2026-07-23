using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using GameBot.Domain.Queues;
using Xunit;

namespace GameBot.UnitTests.Queues;

public sealed class FileQueueRepositoryTests : IDisposable {
  private readonly string _root;

  public FileQueueRepositoryTests() {
    _root = Path.Combine(Path.GetTempPath(), "GameBotQueueTests", Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(_root);
  }

  public void Dispose() {
    try { Directory.Delete(_root, recursive: true); } catch { /* ignore */ }
  }

  private FileQueueRepository NewRepo() => new FileQueueRepository(_root);

  [Fact]
  public async Task CreateAssignsIdAndPersistsConfig() {
    var repo = NewRepo();
    var created = await repo.CreateAsync(new ExecutionQueue { Name = "Farm", EmulatorSerial = "emu-1", CycleExecution = true }).ConfigureAwait(true);

    created.Id.Should().NotBeNullOrWhiteSpace();
    created.CreatedAt.Should().NotBeNull();

    var loaded = await repo.GetAsync(created.Id).ConfigureAwait(true);
    loaded.Should().NotBeNull();
    loaded!.Name.Should().Be("Farm");
    loaded.EmulatorSerial.Should().Be("emu-1");
    loaded.CycleExecution.Should().BeTrue();
  }

  [Fact]
  public async Task PersistedFileContainsConfigOnlyNoEntriesOrStatus() {
    var repo = NewRepo();
    var created = await repo.CreateAsync(new ExecutionQueue { Name = "Farm", EmulatorSerial = "emu-1" }).ConfigureAwait(true);

    var json = await File.ReadAllTextAsync(Path.Combine(_root, "queues", created.Id + ".json")).ConfigureAwait(true);
    json.Should().Contain("emu-1");
    json.Should().NotContain("Entries");
    json.Should().NotContain("Status");
  }

  [Fact]
  public async Task ListReturnsAllCreatedQueues() {
    var repo = NewRepo();
    await repo.CreateAsync(new ExecutionQueue { Name = "A", EmulatorSerial = "emu-1" }).ConfigureAwait(true);
    await repo.CreateAsync(new ExecutionQueue { Name = "B", EmulatorSerial = "emu-2" }).ConfigureAwait(true);

    var all = await repo.ListAsync().ConfigureAwait(true);
    all.Select(q => q.Name).Should().Contain("A").And.Contain("B");
    all.Should().HaveCount(2);
  }

  [Fact]
  public async Task UpdatePersistsNameAndCycle() {
    var repo = NewRepo();
    var created = await repo.CreateAsync(new ExecutionQueue { Name = "A", EmulatorSerial = "emu-1" }).ConfigureAwait(true);

    created.Name = "A2";
    created.CycleExecution = true;
    await repo.UpdateAsync(created).ConfigureAwait(true);

    var loaded = await repo.GetAsync(created.Id).ConfigureAwait(true);
    loaded!.Name.Should().Be("A2");
    loaded.CycleExecution.Should().BeTrue();
  }

  [Fact]
  public async Task DeleteRemovesQueue() {
    var repo = NewRepo();
    var created = await repo.CreateAsync(new ExecutionQueue { Name = "A", EmulatorSerial = "emu-1" }).ConfigureAwait(true);

    (await repo.DeleteAsync(created.Id).ConfigureAwait(true)).Should().BeTrue();
    (await repo.GetAsync(created.Id).ConfigureAwait(true)).Should().BeNull();
  }

  [Fact]
  public async Task UpdatePersistsLinkedTemplateId() {
    var repo = NewRepo();
    var created = await repo.CreateAsync(new ExecutionQueue { Name = "A", EmulatorSerial = "emu-1" }).ConfigureAwait(true);

    created.LinkedTemplateId = "tpl-123";
    await repo.UpdateAsync(created).ConfigureAwait(true);

    var loaded = await repo.GetAsync(created.Id).ConfigureAwait(true);
    loaded!.LinkedTemplateId.Should().Be("tpl-123");
  }

  [Fact]
  public async Task LegacyQueueJsonWithoutLinkedTemplateIdLoadsAsUnlinked() {
    var repo = NewRepo();
    var dir = Path.Combine(_root, "queues");
    Directory.CreateDirectory(dir);
    // A queue document written before this feature: no LinkedTemplateId property.
    await File.WriteAllTextAsync(Path.Combine(dir, "legacy.json"),
      "{\"Id\":\"legacy\",\"Name\":\"Old\",\"EmulatorSerial\":\"emu-1\",\"CycleExecution\":false}").ConfigureAwait(true);

    var loaded = await repo.GetAsync("legacy").ConfigureAwait(true);

    loaded.Should().NotBeNull();
    loaded!.LinkedTemplateId.Should().BeNull();
  }

  [Fact] // T019 — feature 073: idle-pause config round-trips through persistence
  public async Task UpdatePersistsIdlePauseConfig() {
    var repo = NewRepo();
    var created = await repo.CreateAsync(new ExecutionQueue { Name = "A", EmulatorSerial = "emu-1" }).ConfigureAwait(true);

    created.PauseWhenIdle = true;
    created.IdleThresholdSeconds = 45;
    await repo.UpdateAsync(created).ConfigureAwait(true);

    var loaded = await repo.GetAsync(created.Id).ConfigureAwait(true);
    loaded!.PauseWhenIdle.Should().BeTrue();
    loaded.IdleThresholdSeconds.Should().Be(45);
  }

  [Fact] // T019 — a queue JSON written before feature 073 reads as disabled with the default threshold
  public async Task LegacyQueueJsonWithoutIdlePauseFieldsLoadsAsDisabledWithDefaultThreshold() {
    var repo = NewRepo();
    var dir = Path.Combine(_root, "queues");
    Directory.CreateDirectory(dir);
    // A queue document written before feature 073: no PauseWhenIdle/IdleThresholdSeconds properties.
    await File.WriteAllTextAsync(Path.Combine(dir, "legacy2.json"),
      "{\"Id\":\"legacy2\",\"Name\":\"Old\",\"EmulatorSerial\":\"emu-1\",\"CycleExecution\":false}").ConfigureAwait(true);

    var loaded = await repo.GetAsync("legacy2").ConfigureAwait(true);

    loaded.Should().NotBeNull();
    loaded!.PauseWhenIdle.Should().BeFalse();         // absent bool → false
    loaded.IdleThresholdSeconds.Should().Be(30);      // initializer default preserved (back-compat)
  }

  [Fact]
  public async Task GetWithUnsafeIdReturnsNull() {
    var repo = NewRepo();
    (await repo.GetAsync("../escape").ConfigureAwait(true)).Should().BeNull();
  }

  [Fact]
  public async Task CreateWithoutNameThrows() {
    var repo = NewRepo();
    var act = async () => await repo.CreateAsync(new ExecutionQueue { Name = "", EmulatorSerial = "emu-1" }).ConfigureAwait(true);
    await act.Should().ThrowAsync<InvalidOperationException>().ConfigureAwait(true);
  }

  [Fact]
  public async Task CreateWithoutEmulatorThrows() {
    var repo = NewRepo();
    var act = async () => await repo.CreateAsync(new ExecutionQueue { Name = "A", EmulatorSerial = "" }).ConfigureAwait(true);
    await act.Should().ThrowAsync<InvalidOperationException>().ConfigureAwait(true);
  }
}
