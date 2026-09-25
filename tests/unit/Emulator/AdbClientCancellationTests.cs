#pragma warning disable CA2007 // test code: no ConfigureAwait
using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.Versioning;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using GameBot.Emulator.Adb;
using Xunit;

namespace GameBot.UnitTests.Emulator;

/// <summary>
/// Feature 106 (research R-004): a cancel of an ADB call kills the <c>adb</c> process. The fake
/// <c>adb</c> is a copy of <c>ping.exe</c> with a unique name, so that other <c>ping</c> processes
/// cannot make the test fail. It runs for 30 s when nothing kills it.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class AdbClientCancellationTests : IDisposable {
  private readonly string _folder;
  private readonly string _name;
  private readonly string _exePath;

  public AdbClientCancellationTests() {
    _folder = Path.Combine(Path.GetTempPath(), "GameBotAdbKill", Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(_folder);
    _name = "fake-adb-" + Guid.NewGuid().ToString("N");
    _exePath = Path.Combine(_folder, _name + ".exe");
    var ping = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "ping.exe");
    File.Copy(ping, _exePath);
  }

  public void Dispose() {
    foreach (var p in Process.GetProcessesByName(_name)) {
      try { p.Kill(entireProcessTree: true); } catch (InvalidOperationException) { } catch (System.ComponentModel.Win32Exception) { }
      p.Dispose();
    }
    try { Directory.Delete(_folder, recursive: true); }
    catch (IOException) { /* best effort: a process can still hold the file for a moment */ }
    catch (UnauthorizedAccessException) { /* best effort */ }
  }

  private async Task<bool> NoFakeProcessAliveAsync() {
    var sw = Stopwatch.StartNew();
    while (sw.ElapsedMilliseconds < 2000) {
      var procs = Process.GetProcessesByName(_name);
      var alive = procs.Length;
      foreach (var p in procs) p.Dispose();
      if (alive == 0) return true;
      await Task.Delay(50);
    }
    return false;
  }

  [Fact]
  public async Task ExecAsyncKillsTheProcessOnCancel() {
    if (!OperatingSystem.IsWindows()) return;
    var adb = new AdbClient(_exePath);
    using var cts = new CancellationTokenSource(200);

    var sw = Stopwatch.StartNew();
    var act = async () => await adb.ExecAsync("-n 30 127.0.0.1", cts.Token);

    await act.Should().ThrowAsync<OperationCanceledException>();
    sw.ElapsedMilliseconds.Should().BeLessThan(2000);
    (await NoFakeProcessAliveAsync()).Should().BeTrue("the cancel must kill the adb process");
  }

  [Fact]
  public async Task GetScreenshotPngAsyncKillsTheProcessOnCancel() {
    if (!OperatingSystem.IsWindows()) return;
    // AdbClient always adds "exec-out screencap -p", so a batch file wraps the fake. It stays silent
    // and keeps the output pipe open for 30 s.
    var cmdPath = Path.Combine(_folder, _name + ".cmd");
    await File.WriteAllTextAsync(cmdPath, $"@\"{_exePath}\" -n 30 127.0.0.1 >nul\r\n");
    var adb = new AdbClient(cmdPath);
    using var cts = new CancellationTokenSource();

    var call = adb.GetScreenshotPngAsync(cts.Token);
    await Task.Delay(200);
    var sw = Stopwatch.StartNew();
    await cts.CancelAsync();
    var act = async () => await call.WaitAsync(TimeSpan.FromSeconds(10));

    await act.Should().ThrowAsync<OperationCanceledException>();
    sw.ElapsedMilliseconds.Should().BeLessThan(2000, "the read of the screenshot pipe must stop at the cancel");
    (await NoFakeProcessAliveAsync()).Should().BeTrue("the cancel must kill the adb process and its children");
  }
}
