using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using GameBot.Domain.Gamepacks.Managed;
using GameBot.Domain.QueueTemplates;
using GameBot.UnitTests.Gamepacks.Managed;
using Xunit;

#pragma warning disable CA2007, CA1861, CA1707, CA2000, CA1305, CA1307, CA1310, CA1849

namespace GameBot.IntegrationTests.Gamepacks;

/// <summary>Feature 148 (T033, SC-003): new entries are disabled and old entries keep their values.</summary>
public sealed class GamepackManagedEntriesIntegrationTests {
  [Fact]
  public async Task NoNewEntryIsEnabledAndEveryOldEntryKeepsItsValues() {
    using var rig = new ManagedTestRig();
    await rig.InstallAsync("1.0.0", "alpha", "beta");
    var template = await rig.AddTemplateAsync("daily", "alpha", "beta");
    var stored = await rig.TemplateAsync("daily");
    stored.Entries[0].Enabled = true;
    stored.Entries[0].ScheduleType = ScheduleType.Timer;
    stored.Entries[0].TimerTimeOfDay = new System.TimeOnly(4, 0);
    stored.Entries[1].Enabled = false;
    await rig.BaseTemplates.UpdateAsync(stored);
    await rig.AddQueueAsync("Q", template, running: true);

    var report = await rig.RunAsync(ManagedTestRig.Update(ManagedTestRig.Pack("2.0.0", "alpha", "beta", "gamma", "delta")));

    report.Result.Should().Be(ManagedUpdateResults.Done, ManagedTestRig.Describe(report));
    var after = await rig.TemplateAsync("daily");
    after.Entries.Should().HaveCount(4);
    after.Entries.Skip(2).Should().OnlyContain(e => !e.Enabled && e.ScheduleType == ScheduleType.OncePerRun && e.ParameterValues.Count == 0);
    after.Entries[0].Enabled.Should().BeTrue();
    after.Entries[0].ScheduleType.Should().Be(ScheduleType.Timer);
    after.Entries[0].TimerTimeOfDay.Should().Be(new System.TimeOnly(4, 0));
    after.Entries[1].Enabled.Should().BeFalse();
    report.NewEntries.Should().HaveCount(2);
  }

  [Fact]
  public async Task AStoppedQueueIsNotStartedButItsTemplateGetsTheNewDisabledEntries() {
    using var rig = new ManagedTestRig();
    await rig.InstallAsync("1.0.0", "alpha");
    var template = await rig.AddTemplateAsync("daily", "alpha");
    var queue = await rig.AddQueueAsync("Q", template, running: false);

    var report = await rig.RunAsync(ManagedTestRig.Update(ManagedTestRig.Pack("2.0.0", "alpha", "gamma")));

    report.Result.Should().Be(ManagedUpdateResults.Done, ManagedTestRig.Describe(report));
    rig.Control.Running.Should().NotContain(queue.Id);
    rig.Control.StartCalls.Should().BeEmpty();
    (await rig.TemplateAsync("daily")).Entries.Last().Enabled.Should().BeFalse();
  }
}
