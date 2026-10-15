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

/// <summary>Feature 143 (T050): a soft performance check. The threshold is generous. It is not a hard CI gate.</summary>
public sealed class GamepackPerformanceTests : IDisposable {
  private readonly GamepackTestKit _kit = new();

  public void Dispose() => _kit.Dispose();

  [Fact]
  public async Task AnInstallOfOneHundredObjectsTakesLessThanFiveSeconds() {
    var sequences = Enumerable.Range(0, 40).Select(i => GamepackTestKit.Seq("s" + i, "Seq " + i)).ToArray();
    var commands = Enumerable.Range(0, 40).Select(i => GamepackTestKit.Cmd("c" + i, "Cmd " + i)).ToArray();
    var images = Enumerable.Range(0, 20).Select(i => GamepackTestKit.Img("img" + i)).ToArray();
    var json = GamepackTestKit.Pack("big", "1", sequences, commands, images);

    var watch = Stopwatch.StartNew();
    var outcome = await _kit.Service.InstallAsync(json, dryRun: false);
    watch.Stop();

    outcome.Status.Should().Be(GamepackInstallStatus.Ok);
    outcome.Created.Should().Be(100);
    watch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(5));
  }
}
