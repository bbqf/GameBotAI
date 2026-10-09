using FluentAssertions;
using GameBot.Service.Services.Updates;
using Xunit;

namespace GameBot.UnitTests.Updates;

public sealed class InstallLocationGuardTests {
  private const string Folder = @"C:\Users\anton\AppData\Local\GameBot\app";

  private static InstallLocationGuard Guard(string? runValue, string serviceFolder = Folder) =>
    new(() => runValue, serviceFolder);

  [Fact]
  public void SameFolderIsInstalled() {
    Guard($"\"{Folder}\\GameBot.Service.exe\"").IsInstalledHere().Should().BeTrue();
  }

  [Fact]
  public void DifferentFolderIsNotInstalled() {
    Guard("\"C:\\src\\GameBot\\bin\\GameBot.Service.exe\"").IsInstalledHere().Should().BeFalse();
  }

  [Theory]
  [InlineData(null)]
  [InlineData("")]
  [InlineData("   ")]
  public void MissingRegistryValueIsNotInstalled(string? value) {
    Guard(value).IsInstalledHere().Should().BeFalse();
  }

  [Fact]
  public void UnquotedPathWorks() {
    Guard($"{Folder}\\GameBot.Service.exe").IsInstalledHere().Should().BeTrue();
  }

  [Fact]
  public void LetterCaseIsIgnored() {
    Guard($"\"{Folder.ToUpperInvariant()}\\GAMEBOT.SERVICE.EXE\"").IsInstalledHere().Should().BeTrue();
  }

  [Fact]
  public void TrailingSlashOnTheServiceFolderIsIgnored() {
    Guard($"\"{Folder}\\GameBot.Service.exe\"", Folder + "\\").IsInstalledHere().Should().BeTrue();
  }

  [Fact]
  public void ValueWithoutAFolderIsNotInstalled() {
    Guard("GameBot.Service.exe").IsInstalledHere().Should().BeFalse();
  }

  [Fact]
  public void ValueWithInvalidCharactersIsNotInstalled() {
    Guard("\"C:\\bad\0path\\GameBot.Service.exe\"").IsInstalledHere().Should().BeFalse();
  }

  [Fact]
  public void DefaultGuardDoesNotThrow() {
    var act = () => new InstallLocationGuard().IsInstalledHere();

    act.Should().NotThrow();
  }
}
