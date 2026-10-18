using System;
using System.IO;
using System.Threading.Tasks;
using FluentAssertions;
using GameBot.Domain.Images;
using GameBot.UnitTests.Utils;
using Xunit;

#pragma warning disable CA1707, CA2007, CA1861, CA1416, CA2000, CA1849, CA2000, CA1031

namespace GameBot.UnitTests.Images;

/// <summary>Issue #333 (T011, T019, SC-004): an image replace while another handle holds the old file.</summary>
public sealed class ImageRepositoryReplaceLockTests {
  private static async Task<(FileImageRepository Repo, string Path)> ArrangeAsync() {
    var repo = new FileImageRepository(FileHold.NewFolder());
    using var first = new MemoryStream(new byte[] { 1, 2, 3 });
    await repo.SaveAsync("pit-title", first, "image/png", null, overwrite: false);
    return (repo, repo.GetFilePath("pit-title")!);
  }

  [Fact]
  public async Task AShortLockOnTheOldFileDoesNotStopTheReplace() {
    if (!OperatingSystem.IsWindows()) {
      return;
    }

    var (repo, path) = await ArrangeAsync();
    var hold = FileHold.Open(path);
    var release = FileHold.ReleaseAfter(hold, 300);

    using var next = new MemoryStream(new byte[] { 9, 9, 9, 9 });
    await repo.SaveAsync("pit-title", next, "image/png", null, overwrite: true);
    await release;

    (await File.ReadAllBytesAsync(path)).Should().Equal(9, 9, 9, 9);
  }

  [Fact]
  public async Task ALongLockGivesTheFileErrorAndKeepsTheOldContent() {
    if (!OperatingSystem.IsWindows()) {
      return;
    }

    var (repo, path) = await ArrangeAsync();
    using var hold = FileHold.Open(path);

    using var next = new MemoryStream(new byte[] { 9, 9, 9, 9 });
    var act = async () => await repo.SaveAsync("pit-title", next, "image/png", null, overwrite: true);

    var error = (await act.Should().ThrowAsync<IOException>()).Which;
    error.Message.Should().StartWith("Cannot replace file '" + path + "' for image 'pit-title': ");
    Directory.GetFiles(Path.Combine(Path.GetDirectoryName(path)!, ".tmp")).Should().BeEmpty("the temp file is removed");
    hold.Dispose();
    (await File.ReadAllBytesAsync(path)).Should().Equal(1, 2, 3);
  }
}
