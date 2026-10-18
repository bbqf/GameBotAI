using System;
using System.IO;
using System.Threading.Tasks;
using FluentAssertions;
using GameBot.Domain.Images;
using GameBot.UnitTests.Utils;
using Xunit;

#pragma warning disable CA1707, CA2007, CA1861, CA1416, CA2000, CA1849, CA2000, CA1031

namespace GameBot.UnitTests.Images;

/// <summary>Issue #333 (T012, T020): an alternates replace while another handle holds the old file.</summary>
public sealed class ImageAlternatesReplaceLockTests {
  private static (FileImageAlternatesRepository Repo, string Path) Arrange() {
    var repo = new FileImageAlternatesRepository(FileHold.NewFolder());
    repo.SetAlternates("pit-title", new[] { "a1" });
    return (repo, repo.GetFilePath("pit-title")!);
  }

  [Fact]
  public async Task AShortLockOnTheOldFileDoesNotStopTheReplace() {
    if (!OperatingSystem.IsWindows()) {
      return;
    }

    var (repo, path) = Arrange();
    var hold = FileHold.Open(path);
    var release = FileHold.ReleaseAfter(hold, 300);

    repo.SetAlternates("pit-title", new[] { "b1", "b2" });
    await release;

    repo.GetAlternates("pit-title").Should().Equal("b1", "b2");
  }

  [Fact]
  public void ALongLockGivesTheFileError() {
    if (!OperatingSystem.IsWindows()) {
      return;
    }

    var (repo, path) = Arrange();
    using var hold = FileHold.Open(path);

    var act = () => repo.SetAlternates("pit-title", new[] { "b1" });

    act.Should().Throw<IOException>().Which.Message
      .Should().StartWith("Cannot replace file '" + path + "' for image 'pit-title': ");
    hold.Dispose();
    repo.GetAlternates("pit-title").Should().Equal("a1");
  }
}
