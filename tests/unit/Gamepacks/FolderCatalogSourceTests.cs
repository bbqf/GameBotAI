using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using FluentAssertions;
using GameBot.Domain.Gamepacks.Catalog;
using Xunit;

#pragma warning disable CA2007, CA1861, CA1707, CA1305, CA1307, CA1310, CA1849

namespace GameBot.UnitTests.Gamepacks;

/// <summary>Feature 147 (T016): the folder source.</summary>
public sealed class FolderCatalogSourceTests : IDisposable {
  private readonly string _root = Path.Combine(Path.GetTempPath(), "GameBotCatalogFolder", Guid.NewGuid().ToString("N"));
  private readonly string _outside;

  public FolderCatalogSourceTests() {
    Directory.CreateDirectory(_root);
    _outside = _root + "-outside";
    Directory.CreateDirectory(_outside);
  }

  public void Dispose() {
    var link = Path.Combine(_root, "link");
    if (Directory.Exists(link)) {
      Directory.Delete(link);
    }

    Directory.Delete(_root, recursive: true);
    Directory.Delete(_outside, recursive: true);
  }

  private FolderCatalogSource Source(CatalogOptions? options = null) => new(_root, options ?? new CatalogOptions());

  [Fact]
  public async Task ItReadsTheIndexAndAFile() {
    File.WriteAllText(Path.Combine(_root, "index.json"), "{}");
    Directory.CreateDirectory(Path.Combine(_root, "p"));
    File.WriteAllText(Path.Combine(_root, "p", "a.json"), "hello");

    (await Source().ReadIndexAsync(default)).Should().Equal(Encoding.UTF8.GetBytes("{}"));
    (await Source().ReadFileAsync("p/a.json", 100, default)).Should().Equal(Encoding.UTF8.GetBytes("hello"));
  }

  [Fact]
  public async Task AMissingIndexGivesIndexInvalid() {
    var act = () => Source().ReadIndexAsync(default);

    (await act.Should().ThrowAsync<CatalogException>()).Which.Code.Should().Be(CatalogFailure.IndexInvalid);
  }

  [Fact]
  public async Task AMissingFileGivesFileMissing() {
    var act = () => Source().ReadFileAsync("none.json", 100, default);

    (await act.Should().ThrowAsync<CatalogException>()).Which.Code.Should().Be(CatalogFailure.FileMissing);
  }

  [Fact]
  public async Task AFileOutsideTheFolderIsRefused() {
    File.WriteAllText(Path.Combine(_outside, "secret.json"), "secret");

    var act = () => Source().ReadFileAsync("../" + Path.GetFileName(_outside) + "/secret.json", 100, default);

    (await act.Should().ThrowAsync<CatalogException>()).Which.Code.Should().Be(CatalogFailure.FileMissing);
  }

  [Fact]
  public async Task ALargeFileGivesFileTooLargeAndALargeIndexGivesIndexTooLarge() {
    File.WriteAllText(Path.Combine(_root, "index.json"), new string('x', 50));
    File.WriteAllText(Path.Combine(_root, "big.json"), new string('x', 50));
    var options = new CatalogOptions { IndexLimitBytes = 10 };

    (await FluentActions.Awaiting(() => Source(options).ReadIndexAsync(default)).Should().ThrowAsync<CatalogException>())
      .Which.Code.Should().Be(CatalogFailure.IndexTooLarge);
    (await FluentActions.Awaiting(() => Source(options).ReadFileAsync("big.json", 10, default)).Should().ThrowAsync<CatalogException>())
      .Which.Code.Should().Be(CatalogFailure.FileTooLarge);
  }

  [Fact]
  public async Task AJunctionInsideTheFolderIsRefusedWhenTheOsAllowsIt() {
    File.WriteAllText(Path.Combine(_outside, "secret.json"), "secret");
    var link = Path.Combine(_root, "link");
    if (!TryMakeLink(link, _outside)) {
      return;
    }

    var act = () => Source().ReadFileAsync("link/secret.json", 100, default);

    (await act.Should().ThrowAsync<CatalogException>()).Which.Code.Should().Be(CatalogFailure.FileMissing);
  }

  [Fact]
  public async Task AFolderCatalogNeedsNoTokenAndIgnoresInsecure() {
    var settings = new FakeSettings(new GamepackCatalogSettings { Location = _root, AllowInsecure = false, Token = "ignored" });
    File.WriteAllText(Path.Combine(_root, "index.json"), "{}");
    var factory = new GamepackCatalogSourceFactory(settings, () => throw new InvalidOperationException("No HTTP client for a folder."), new CatalogOptions());

    var source = factory.Create();

    source.Should().BeOfType<FolderCatalogSource>();
    (await source.ReadIndexAsync(default)).Should().NotBeEmpty();
  }

  private static bool TryMakeLink(string link, string target) {
    try {
      if (OperatingSystem.IsWindows()) {
        using var process = Process.Start(new ProcessStartInfo("cmd.exe", $"/c mklink /J \"{link}\" \"{target}\"") { CreateNoWindow = true, UseShellExecute = false, RedirectStandardOutput = true });
        process!.WaitForExit();
        return process.ExitCode == 0;
      }

      Directory.CreateSymbolicLink(link, target);
      return true;
    }
    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception) {
      return false;
    }
  }

  internal sealed class FakeSettings : IGamepackCatalogSettingsStore {
    private GamepackCatalogSettings _settings;

    public FakeSettings(GamepackCatalogSettings settings) => _settings = settings;

    public GamepackCatalogSettings Load() => _settings;

    public GamepackCatalogSettings Save(string? location, bool allowInsecure, string? token) =>
      _settings = new GamepackCatalogSettings { Location = location, AllowInsecure = allowInsecure, Token = token };
  }
}
