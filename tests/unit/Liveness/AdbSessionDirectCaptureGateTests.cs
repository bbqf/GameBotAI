#pragma warning disable CA2007 // test code: no ConfigureAwait
using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using GameBot.Emulator;
using GameBot.Service.Services.Liveness;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace GameBot.UnitTests.Liveness;

/// <summary>
/// Feature 121 (research R-014): the direct capture of the liveness probe asks the gate. It returns false
/// at once, and starts no <c>screencap</c> process, for a Suspect serial.
/// </summary>
public sealed class AdbSessionDirectCaptureGateTests {
  [Fact]
  public async Task ASuspectSerialGivesFalseAtOnce() {
    var gate = new DeviceCaptureGate();
    gate.TryBegin("emu-suspect");
    gate.TimedOut("emu-suspect");
    var sut = new AdbSessionDirectCapture(NullLogger<GameBot.Emulator.Adb.AdbClient>.Instance, gate);
    var sw = Stopwatch.StartNew();

    var ok = await sut.TryCaptureAsync("emu-suspect", CancellationToken.None);

    ok.Should().BeFalse();
    sw.ElapsedMilliseconds.Should().BeLessThan(500, "no adb process starts for a suspect device");
  }

  [Fact]
  public async Task TheProbeDoesNotChangeTheGateState() {
    var gate = new DeviceCaptureGate();
    gate.TryBegin("emu-suspect");
    gate.TimedOut("emu-suspect");
    var sut = new AdbSessionDirectCapture(NullLogger<GameBot.Emulator.Adb.AdbClient>.Instance, gate);

    await sut.TryCaptureAsync("emu-suspect", CancellationToken.None);
    await sut.TryCaptureAsync("emu-suspect", CancellationToken.None);

    gate.IsSuspect("emu-suspect").Should().BeTrue();
  }

  [Fact]
  public async Task ABlankSerialDoesNotThrow() {
    var sut = new AdbSessionDirectCapture(NullLogger<GameBot.Emulator.Adb.AdbClient>.Instance, new DeviceCaptureGate());

    var act = async () => await sut.TryCaptureAsync(" ", CancellationToken.None);

    await act.Should().NotThrowAsync();
  }
}
