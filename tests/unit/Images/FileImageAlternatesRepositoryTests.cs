#pragma warning disable CA2007, CA1861, CA1859, CA2000, CA1849
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using GameBot.Domain.Images;
using Xunit;

namespace GameBot.UnitTests.Images {
  public sealed class FileImageAlternatesRepositoryTests : IDisposable {
    private readonly string _root = Path.Combine(Path.GetTempPath(), "gamebot-alts-" + Guid.NewGuid().ToString("N"));

    public void Dispose() {
      if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public void SetThenGetPreservesOrder() {
      var repo = new FileImageAlternatesRepository(_root);
      repo.SetAlternates("primary", new[] { "night-2", "night-1", "night-3" });

      repo.GetAlternates("primary").Should().Equal("night-2", "night-1", "night-3");
    }

    [Fact]
    public void SetReplacesTheWholeList() {
      var repo = new FileImageAlternatesRepository(_root);
      repo.SetAlternates("primary", new[] { "a", "b" });
      repo.SetAlternates("primary", new[] { "c" });

      repo.GetAlternates("primary").Should().Equal("c");
    }

    [Fact]
    public void AbsentListReadsAsEmpty() {
      var repo = new FileImageAlternatesRepository(_root);

      repo.GetAlternates("primary").Should().BeEmpty();
    }

    [Fact]
    public void EmptyListDeletesTheSidecar() {
      var repo = new FileImageAlternatesRepository(_root);
      repo.SetAlternates("primary", new[] { "a" });
      File.Exists(Path.Combine(_root, ".alternates", "primary.json")).Should().BeTrue();

      repo.SetAlternates("primary", Array.Empty<string>());

      File.Exists(Path.Combine(_root, ".alternates", "primary.json")).Should().BeFalse();
      repo.GetAlternates("primary").Should().BeEmpty();
    }

    [Fact]
    public void DeleteRemovesTheList() {
      var repo = new FileImageAlternatesRepository(_root);
      repo.SetAlternates("primary", new[] { "a" });

      repo.DeleteAlternates("primary").Should().BeTrue();

      repo.GetAlternates("primary").Should().BeEmpty();
      repo.DeleteAlternates("primary").Should().BeFalse();
    }

    [Fact]
    public void ListSurvivesANewRepositoryInstance() {
      new FileImageAlternatesRepository(_root).SetAlternates("primary", new[] { "a", "b" });

      new FileImageAlternatesRepository(_root).GetAlternates("primary").Should().Equal("a", "b");
    }

    [Fact]
    public void InvalidPrimaryIdIsRejected() {
      var repo = new FileImageAlternatesRepository(_root);

      var act = () => repo.SetAlternates("..\\escape", new[] { "a" });

      act.Should().Throw<ArgumentException>();
      repo.GetAlternates("..\\escape").Should().BeEmpty();
      repo.DeleteAlternates("..\\escape").Should().BeFalse();
    }

    [Fact]
    public void WritesLeaveNoTemporaryFilesBehind() {
      var repo = new FileImageAlternatesRepository(_root);
      repo.SetAlternates("primary", new[] { "a" });
      repo.SetAlternates("primary", new[] { "b" });

      var tmp = Path.Combine(_root, ".tmp");
      (Directory.Exists(tmp) ? Directory.GetFiles(tmp) : Array.Empty<string>()).Should().BeEmpty();
    }

    [Fact]
    public async Task SidecarsAreNeverListedAsImages() {
      var repo = new FileImageAlternatesRepository(_root);
      repo.SetAlternates("primary", new[] { "a" });
      var images = new FileImageRepository(_root);

      var ids = await images.ListIdsAsync();

      ids.Should().NotContain(id => id.Contains("primary", StringComparison.OrdinalIgnoreCase));
      ids.Should().BeEmpty();
    }
  }
}
