using System.IO;
using FluentAssertions;
using GameBot.Domain.Commands;
using Xunit;

namespace GameBot.IntegrationTests.Sequences;

/// <summary>Feature 189: the sequence setting is stored in the file, false for old files, and absent from the file when false.</summary>
[Collection("ConfigIsolation")]
public sealed class IgnoreSmartSchedulingPersistenceTests {
  [Fact]
  public async Task TheSettingSurvivesARepositoryReload() {
    var root = Path.Combine(Path.GetTempPath(), $"gamebot-ignore-{Guid.NewGuid():N}");
    try {
      var first = new FileSequenceRepository(root);
      await first.CreateAsync(new CommandSequence { Id = "critical", Name = "Critical", IgnoreSmartScheduling = true }).ConfigureAwait(false);
      await first.CreateAsync(new CommandSequence { Id = "plain", Name = "Plain" }).ConfigureAwait(false);

      var second = new FileSequenceRepository(root);

      (await second.GetAsync("critical").ConfigureAwait(false))!.IgnoreSmartScheduling.Should().BeTrue();
      (await second.GetAsync("plain").ConfigureAwait(false))!.IgnoreSmartScheduling.Should().BeFalse();
    }
    finally {
      if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
    }
  }

  [Fact]
  public async Task AnOldFileWithNoMemberReadsAsFalse() {
    var root = Path.Combine(Path.GetTempPath(), $"gamebot-ignore-{Guid.NewGuid():N}");
    try {
      var repo = new FileSequenceRepository(root);
      var dir = Path.Combine(root, "commands", "sequences");
      await File.WriteAllTextAsync(Path.Combine(dir, "old.json"), "{\"id\":\"old\",\"name\":\"Old\",\"version\":3,\"steps\":[]}").ConfigureAwait(false);

      var loaded = await repo.GetAsync("old").ConfigureAwait(false);

      loaded.Should().NotBeNull();
      loaded!.IgnoreSmartScheduling.Should().BeFalse();
    }
    finally {
      if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
    }
  }

  [Fact]
  public async Task AStoredFileHasNoMemberWhenTheValueIsFalse() {
    var root = Path.Combine(Path.GetTempPath(), $"gamebot-ignore-{Guid.NewGuid():N}");
    try {
      var repo = new FileSequenceRepository(root);
      await repo.CreateAsync(new CommandSequence { Id = "plain", Name = "Plain" }).ConfigureAwait(false);
      await repo.CreateAsync(new CommandSequence { Id = "critical", Name = "Critical", IgnoreSmartScheduling = true }).ConfigureAwait(false);
      var dir = Path.Combine(root, "commands", "sequences");

      (await File.ReadAllTextAsync(Path.Combine(dir, "plain.json")).ConfigureAwait(false)).Should().NotContain("ignoreSmartScheduling");
      (await File.ReadAllTextAsync(Path.Combine(dir, "critical.json")).ConfigureAwait(false)).Should().MatchRegex("\"ignoreSmartScheduling\":\\s*true");
    }
    finally {
      if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
    }
  }
}
