using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using GameBot.Domain.Notifications;
using GameBot.Domain.Queues;
using Xunit;

#pragma warning disable CA2007, CA1861, CA1707

namespace GameBot.UnitTests.Notifications;

/// <summary>Feature 120 (V-01): the queue level in the queue file.</summary>
public sealed class QueueLevelPersistenceTests : IDisposable {
  private readonly string _root = Path.Combine(Path.GetTempPath(), "gamebot-level-" + Guid.NewGuid().ToString("N"));

  public QueueLevelPersistenceTests() {
    Directory.CreateDirectory(_root);
  }

  public void Dispose() {
    try { Directory.Delete(_root, recursive: true); }
    catch (IOException) { /* best effort */ }
    GC.SuppressFinalize(this);
  }

  [Fact]
  public async Task V01_AQueueFileWithNoLevelReadsAsNone() {
    var repo = new FileQueueRepository(_root);
    await File.WriteAllTextAsync(Path.Combine(_root, "queues", "old.json"),
      "{\"Id\":\"old\",\"Name\":\"Old\",\"EmulatorSerial\":\"emu-1\",\"CycleExecution\":false}");

    var queue = await repo.GetAsync("old");

    queue.Should().NotBeNull();
    queue!.Id.Should().Be("old");
    queue.NotificationLevel.Should().Be(NotificationLevel.None);
  }

  [Theory]
  [InlineData(NotificationLevel.None, "none")]
  [InlineData(NotificationLevel.Failure, "failure")]
  [InlineData(NotificationLevel.SuccessAndFailure, "successAndFailure")]
  public async Task V01_ASavedLevelSurvivesARestartAndIsWrittenAsText(NotificationLevel level, string text) {
    var repo = new FileQueueRepository(_root);
    await repo.CreateAsync(new ExecutionQueue { Id = "q1", Name = "Q", EmulatorSerial = "emu-1", NotificationLevel = level });

    var reread = await new FileQueueRepository(_root).GetAsync("q1");

    reread!.NotificationLevel.Should().Be(level);
    using var doc = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(_root, "queues", "q1.json")));
    doc.RootElement.GetProperty("NotificationLevel").GetString().Should().Be(text);
  }

  [Fact]
  public async Task AnUpdateThatBuildsOnTheStoredQueueKeepsTheLevel() {
    var repo = new FileQueueRepository(_root);
    await repo.CreateAsync(new ExecutionQueue { Id = "q1", Name = "Q", EmulatorSerial = "emu-1", NotificationLevel = NotificationLevel.Failure });

    var stored = (await repo.GetAsync("q1"))!;
    stored.Name = "Renamed";
    await repo.UpdateAsync(stored);

    (await repo.GetAsync("q1"))!.NotificationLevel.Should().Be(NotificationLevel.Failure);
  }

  [Theory]
  [InlineData("none", true, NotificationLevel.None)]
  [InlineData("FAILURE", true, NotificationLevel.Failure)]
  [InlineData(" successAndFailure ", true, NotificationLevel.SuccessAndFailure)]
  [InlineData("all", false, NotificationLevel.None)]
  [InlineData("", false, NotificationLevel.None)]
  [InlineData(null, false, NotificationLevel.None)]
  public void TryParseReadsTheThreeValuesOnly(string? text, bool ok, NotificationLevel expected) {
    NotificationLevelText.TryParse(text, out var level).Should().Be(ok);
    level.Should().Be(expected);
  }

  [Theory]
  [InlineData(NotificationLevel.None, "none")]
  [InlineData(NotificationLevel.Failure, "failure")]
  [InlineData(NotificationLevel.SuccessAndFailure, "successAndFailure")]
  [InlineData((NotificationLevel)99, "none")]
  public void ToTextGivesTheJsonText(NotificationLevel level, string expected) {
    NotificationLevelText.ToText(level).Should().Be(expected);
  }
}
