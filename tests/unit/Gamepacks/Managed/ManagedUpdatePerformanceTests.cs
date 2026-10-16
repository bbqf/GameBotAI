using System;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using GameBot.Domain.Gamepacks.Managed;
using Xunit;

#pragma warning disable CA2007, CA1707, CA1861

namespace GameBot.UnitTests.Gamepacks.Managed;

/// <summary>Feature 148 (T052): the flow adds under 2 s of its own time for 20 queues and 20 templates.</summary>
public sealed class ManagedUpdatePerformanceTests {
  [Fact]
  public async Task TheFlowAndTheDryRunStayUnderTwoSecondsForTwentyQueuesAndTwentyTemplates() {
    using var rig = new ManagedTestRig();
    await rig.InstallAsync("1.0.0", "alpha", "beta");
    for (var i = 0; i < 20; i++) {
      var template = await rig.AddTemplateAsync("t" + i, "alpha", "beta");
      await rig.AddQueueAsync("q" + i, template, running: true);
    }

    var timer = Stopwatch.StartNew();
    var dry = await rig.RunAsync(ManagedTestRig.Update(ManagedTestRig.Pack("2.0.0", "alpha", "gamma"), dryRun: true));
    var dryTime = timer.Elapsed;
    timer.Restart();
    var real = await rig.RunAsync(ManagedTestRig.Update(ManagedTestRig.Pack("2.0.0", "alpha", "gamma")));
    var realTime = timer.Elapsed;

    dry.Result.Should().Be(ManagedUpdateResults.Planned, ManagedTestRig.Describe(dry));
    real.Result.Should().Be(ManagedUpdateResults.Done, ManagedTestRig.Describe(real));
    real.StoppedQueues.Should().HaveCount(20);
    real.RestartedQueues.Should().HaveCount(20);
    real.NewEntries.Select(e => e.TemplateName).Distinct().Should().HaveCount(20);
    dryTime.Should().BeLessThan(TimeSpan.FromSeconds(2));
    realTime.Should().BeLessThan(TimeSpan.FromSeconds(2));
  }
}
