using System;
using System.IO;
using System.Threading.Tasks;
using FluentAssertions;
using GameBot.Domain.Gamepacks;
using Xunit;

#pragma warning disable CA1707, CA2007, CA1861, CA1416, CA2000, CA1849, CA2000, CA1031

namespace GameBot.UnitTests.Gamepacks;

/// <summary>Issue #333 (T025, FR-012): a read-only target file fails at once, names the file, and the other objects keep the old content.</summary>
public sealed class GamepackApplierReadOnlyTargetTests {
  [Fact]
  public async Task AReadOnlyImageFailsAtOnceAndKeepsTheOtherObjects() {
    if (!OperatingSystem.IsWindows()) {
      return;
    }

    var (kit, _, _, plan) = await GamepackApplierWriteErrorTests.ArrangeAsync();
    using var _ = kit;
    var path = kit.Images.GetFilePath("img")!;
    var before = kit.Snapshot();
    File.SetAttributes(path, FileAttributes.ReadOnly);
    try {
      var started = DateTime.UtcNow;
      var outcome = await kit.Applier.ApplyAsync(plan, GamepackPreviousHandling.Keep);

      outcome.Status.Should().NotBe(GamepackApplyStatus.Applied);
      outcome.Error!.Message.Should().StartWith($"Cannot replace file '{path}' for image 'img': ");
      (DateTime.UtcNow - started).TotalMilliseconds.Should().BeLessThan(1000, "a read-only file is not retried");
      kit.Snapshot().Should().BeEquivalentTo(before);
    }
    finally {
      File.SetAttributes(path, FileAttributes.Normal);
    }
  }
}
