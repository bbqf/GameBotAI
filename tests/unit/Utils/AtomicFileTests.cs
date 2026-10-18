using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using FluentAssertions;
using GameBot.Domain.Utils;
using Xunit;

#pragma warning disable CA1707, CA2007, CA1861, CA1416, CA1849, CA2000, CA1031

namespace GameBot.UnitTests.Utils;

/// <summary>Issue #333 (T004): the replace helper for the install path.</summary>
public sealed class AtomicFileTests {
  private static (string Dir, string Temp, string Target) Arrange(bool targetExists) {
    var dir = FileHold.NewFolder();
    var temp = Path.Combine(dir, "new.tmp");
    var target = Path.Combine(dir, "item.json");
    File.WriteAllText(temp, "new");
    if (targetExists) {
      File.WriteAllText(target, "old");
    }

    return (dir, temp, target);
  }

  [Fact]
  public void ANewTargetGetsTheContent() {
    var (_, temp, target) = Arrange(targetExists: false);

    AtomicFile.ReplaceFile(temp, target, "image", "item");

    File.ReadAllText(target).Should().Be("new");
    File.Exists(temp).Should().BeFalse();
  }

  [Fact]
  public void AnExistingTargetIsReplaced() {
    var (_, temp, target) = Arrange(targetExists: true);

    AtomicFile.ReplaceFile(temp, target, "image", "item");

    File.ReadAllText(target).Should().Be("new");
    File.Exists(temp).Should().BeFalse();
  }

  [Fact]
  public async Task AShortLockIsRetried() {
    if (!OperatingSystem.IsWindows()) {
      return;
    }

    var (_, temp, target) = Arrange(targetExists: true);
    var hold = FileHold.Open(target);
    var release = FileHold.ReleaseAfter(hold, 300);

    AtomicFile.ReplaceFile(temp, target, "image", "item");
    await release;

    File.ReadAllText(target).Should().Be("new");
  }

  [Fact]
  public void ALongLockGivesTheFixedMessageAndRemovesTheTempFile() {
    if (!OperatingSystem.IsWindows()) {
      return;
    }

    var (_, temp, target) = Arrange(targetExists: true);
    using var hold = FileHold.Open(target);

    var act = () => AtomicFile.ReplaceFile(temp, target, "image", "item");

    var error = act.Should().Throw<IOException>().Which;
    error.Message.Should().StartWith("Cannot replace file '" + Path.GetFullPath(target) + "' for image 'item': ");
    error.InnerException.Should().NotBeNull();
    File.Exists(temp).Should().BeFalse();
    File.ReadAllText(target).Should().Be("old");
  }

  [Fact]
  public void AReadOnlyTargetFailsAtOnceAndNamesTheFile() {
    if (!OperatingSystem.IsWindows()) {
      return;
    }

    var (_, temp, target) = Arrange(targetExists: true);
    File.SetAttributes(target, FileAttributes.ReadOnly);
    try {
      var watch = Stopwatch.StartNew();
      var act = () => AtomicFile.ReplaceFile(temp, target, "sequence", "s1");
      var error = act.Should().Throw<IOException>().Which;
      watch.Stop();

      error.Message.Should().StartWith("Cannot replace file '" + Path.GetFullPath(target) + "' for sequence 's1': ");
      watch.ElapsedMilliseconds.Should().BeLessThan(50, "a read-only target is not retried");
      File.Exists(temp).Should().BeFalse();
    }
    finally {
      File.SetAttributes(target, FileAttributes.Normal);
    }
  }
}
