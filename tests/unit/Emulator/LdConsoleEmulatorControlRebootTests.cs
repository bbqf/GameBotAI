#pragma warning disable CA2007 // test code: no ConfigureAwait
using System;
using System.IO;
using System.Threading.Tasks;
using FluentAssertions;
using GameBot.Service.Services.EnsureEmulatorRunning;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace GameBot.UnitTests.Emulator;

/// <summary>
/// Feature 121 (research R-007): <c>RebootAsync</c> returns a result. The fake tool is a small batch
/// file that ends with a chosen exit code.
/// </summary>
public sealed class LdConsoleEmulatorControlRebootTests : IDisposable {
  private readonly string _dir = Path.Combine(Path.GetTempPath(), "gb-reboot-" + Guid.NewGuid().ToString("N"));

  public LdConsoleEmulatorControlRebootTests() {
    Directory.CreateDirectory(_dir);
  }

  public void Dispose() {
    try {
      Directory.Delete(_dir, recursive: true);
    }
    catch (IOException) {
      // The temporary folder is not important.
    }
  }

  private string FakeTool(int exitCode) {
    var path = Path.Combine(_dir, $"fake-ldconsole-{exitCode}.cmd");
    File.WriteAllText(path, $"@echo off\r\nexit /b {exitCode}\r\n");
    return path;
  }

  private static LdConsoleEmulatorControl Control(string? path) =>
    new(NullLogger<LdConsoleEmulatorControl>.Instance, path);

  [Fact]
  public async Task ExitCodeZeroGivesTrue() {
    if (!OperatingSystem.IsWindows()) return;

    (await Control(FakeTool(0)).RebootAsync("LDPlayer-1", null)).Should().BeTrue();
  }

  [Fact]
  public async Task ANonZeroExitCodeGivesFalse() {
    if (!OperatingSystem.IsWindows()) return;

    (await Control(FakeTool(3)).RebootAsync("LDPlayer-1", null)).Should().BeFalse();
  }

  [Fact]
  public async Task AMissingToolGivesFalse() {
    (await Control(null).RebootAsync("LDPlayer-1", null)).Should().BeFalse();
  }

  [Fact]
  public async Task AStartFailureGivesFalse() {
    if (!OperatingSystem.IsWindows()) return;

    (await Control(Path.Combine(_dir, "does-not-exist.exe")).RebootAsync("LDPlayer-1", null)).Should().BeFalse();
  }
}
