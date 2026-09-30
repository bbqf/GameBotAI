#pragma warning disable CA2007 // test code: no ConfigureAwait
using System;
using System.IO;
using System.Runtime.Versioning;
using System.Threading.Tasks;
using FluentAssertions;
using GameBot.Emulator.Adb;
using Xunit;

namespace GameBot.UnitTests.Emulator;

/// <summary>
/// Feature 121 (research R-013): <c>HasRunningScreencapAsync</c> asks the device with
/// <c>adb shell pidof screencap</c>. The fake <c>adb</c> is a batch file that prints a chosen answer.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class AdbClientScreencapCheckTests : IDisposable {
  private readonly string _folder = Path.Combine(Path.GetTempPath(), "GameBotScreencapCheck", Guid.NewGuid().ToString("N"));

  public AdbClientScreencapCheckTests() {
    Directory.CreateDirectory(_folder);
  }

  public void Dispose() {
    try { Directory.Delete(_folder, recursive: true); }
    catch (IOException) { /* best effort */ }
    catch (UnauthorizedAccessException) { /* best effort */ }
  }

  private AdbClient Fake(string script) {
    var path = Path.Combine(_folder, "fake-adb-" + Guid.NewGuid().ToString("N") + ".cmd");
    File.WriteAllText(path, "@echo off\r\n" + script + "\r\n");
    return new AdbClient(path);
  }

  [Fact]
  public async Task AProcessIdGivesTrue() {
    if (!OperatingSystem.IsWindows()) return;

    (await Fake("echo 1234").HasRunningScreencapAsync(5000)).Should().BeTrue();
  }

  [Fact]
  public async Task SeveralProcessIdsGiveTrue() {
    if (!OperatingSystem.IsWindows()) return;

    (await Fake("echo 1234 5678").HasRunningScreencapAsync(5000)).Should().BeTrue();
  }

  [Fact]
  public async Task AnEmptyAnswerWithACleanExitGivesFalse() {
    if (!OperatingSystem.IsWindows()) return;

    (await Fake("exit /b 0").HasRunningScreencapAsync(5000)).Should().BeFalse();
  }

  [Fact]
  public async Task AnEmptyAnswerWithExitCodeOneGivesFalse() {
    if (!OperatingSystem.IsWindows()) return;

    (await Fake("exit /b 1").HasRunningScreencapAsync(5000)).Should().BeFalse();
  }

  [Fact]
  public async Task AnErrorGivesUnknown() {
    if (!OperatingSystem.IsWindows()) return;

    (await Fake("echo error: device offline 1>&2\r\nexit /b 1").HasRunningScreencapAsync(5000)).Should().BeNull();
  }

  [Fact]
  public async Task AnOtherExitCodeGivesUnknown() {
    if (!OperatingSystem.IsWindows()) return;

    (await Fake("exit /b 7").HasRunningScreencapAsync(5000)).Should().BeNull();
  }

  [Fact]
  public async Task ATimeoutGivesUnknown() {
    if (!OperatingSystem.IsWindows()) return;

    (await Fake("ping -n 6 127.0.0.1 >nul").HasRunningScreencapAsync(300)).Should().BeNull();
  }

  [Theory]
  [InlineData(0, "", "", false)]
  [InlineData(1, "", "", false)]
  [InlineData(0, "42\r\n", "", true)]
  [InlineData(0, "abc", "", null)]
  [InlineData(1, "", "error: closed", null)]
  [InlineData(255, "", "", null)]
  public void ParsePidofAnswerFollowsTheRules(int exit, string stdout, string stderr, bool? expected) {
    AdbClient.ParsePidofAnswer(exit, stdout, stderr).Should().Be(expected);
  }
}
