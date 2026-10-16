using System;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using GameBot.Domain.Gamepacks;
using GameBot.UnitTests.Gamepacks;
using Xunit;

#pragma warning disable CA2007, CA1861, CA1707, CA1416

namespace GameBot.IntegrationTests.Gamepacks;

/// <summary>
/// Feature 149 (T050, SC-001): a take-over over 13 sequences and 131 commands takes at most 2 times as long as a normal install
/// of the same pack. The test uses no fixed time. It compares two runs on one machine, with fresh data folders and a warm-up.
/// </summary>
public sealed class GamepackTakeOverPerformanceTests {
  private const int Rounds = 3;

  private static async Task<TimeSpan> TimeNormalInstallAsync() {
    using var kit = new GamepackTestKit();
    var watch = Stopwatch.StartNew();
    var outcome = await kit.Service.InstallAsync(GamepackTakeOverLargeCaseTests.BigPack(), dryRun: false);
    watch.Stop();
    outcome.Status.Should().Be(GamepackInstallStatus.Ok);
    outcome.Created.Should().Be(145);
    return watch.Elapsed;
  }

  private static async Task<TimeSpan> TimeTakeOverInstallAsync() {
    using var kit = new GamepackTestKit();
    await kit.AddUserObjectsAsync(GamepackTakeOverLargeCaseTests.UserPack());
    var watch = Stopwatch.StartNew();
    var outcome = await kit.InstallTakeOverAsync(GamepackTakeOverLargeCaseTests.BigPack());
    watch.Stop();
    outcome.Status.Should().Be(GamepackInstallStatus.Ok);
    outcome.TakenOver.Should().Be(144);
    return watch.Elapsed;
  }

  [Fact]
  public async Task ATakeOverOfOneHundredFortyFourObjectsTakesAtMostTwiceAsLongAsANormalInstall() {
    await TimeNormalInstallAsync();
    await TimeTakeOverInstallAsync();

    var normal = TimeSpan.MaxValue;
    var takeOver = TimeSpan.MaxValue;
    for (var i = 0; i < Rounds; i++) {
      var n = await TimeNormalInstallAsync();
      var t = await TimeTakeOverInstallAsync();
      normal = n < normal ? n : normal;
      takeOver = t < takeOver ? t : takeOver;
    }

    takeOver.TotalMilliseconds.Should().BeLessThanOrEqualTo(normal.TotalMilliseconds * 2, $"normal={normal.TotalMilliseconds:F0} ms, take-over={takeOver.TotalMilliseconds:F0} ms");
  }
}
