using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using GameBot.Domain.Gamepacks.Managed;
using Xunit;

#pragma warning disable CA2007, CA1707, CA1861

namespace GameBot.UnitTests.Gamepacks.Managed;

/// <summary>Feature 148 (T024, SC-006): a done report has every item of FR-011.</summary>
public sealed class ManagedUpdateReportCompletenessTests {
  [Fact]
  public async Task ADoneReportHasAllItemsWithTheExpectedValues() {
    using var rig = new ManagedTestRig();
    await rig.InstallAsync("1.0.0", "alpha", "beta");
    var template = await rig.AddTemplateAsync("daily", "alpha", "beta");
    var queue = await rig.AddQueueAsync("Q", template, running: true);

    var report = await rig.RunAsync(ManagedTestRig.Update(ManagedTestRig.Pack("2.0.0", "alpha", "gamma")));

    report.Result.Should().Be(ManagedUpdateResults.Done, ManagedTestRig.Describe(report));
    report.StoppedQueues.Should().ContainSingle().Which.Should().Be(new QueueRef(queue.Id, "Q", "daily"));
    report.RestartedQueues.Should().ContainSingle().Which.Id.Should().Be(queue.Id);
    report.NotRestartedQueues.Should().BeEmpty();
    report.BackupId.Should().MatchRegex("^[0-9]{8}T[0-9]{9}-pns$");
    report.Created.Should().Be(1);
    report.Removed.Should().Be(1);
    report.Updated.Should().BeGreaterThan(0);
    report.NewEntries.Should().ContainSingle().Which.Should().Be(new EntryRef("daily", "gamma", 2));
    report.RemovedSequenceEntries.Should().ContainSingle().Which.Should().Be(new EntryRef("daily", "beta", 1));
    report.NotAffectedTemplates.Should().BeEmpty();
    report.Problems.Should().BeEmpty();
    report.Restore.Happened.Should().BeFalse();
    report.DryRun.Should().BeFalse();

    // The entry whose sequence the new version removed stays, unchanged.
    var after = await rig.TemplateAsync("daily");
    after.Entries.Should().HaveCount(3);
    after.Entries.Take(2).Should().OnlyContain(e => e.Enabled);
  }

  [Fact]
  public async Task ATakeOverReportHasTheCountTheBackupIdAndTheDryRunList() {
    using var rig = new ManagedTestRig();
    await rig.Packs.AddUserObjectsAsync(GamepackTestKit.Pack("u", "1", sequences: new[] { GamepackTestKit.Seq("u-alpha", "alpha") }));
    var file = ManagedTestRig.Pack("1.0.0", "alpha");

    var planned = await rig.RunAsync(new ManagedUpdateRequest { Mode = ManagedUpdateMode.Update, FileText = file, DryRun = true, TakeOver = true });
    var done = await rig.RunAsync(new ManagedUpdateRequest { Mode = ManagedUpdateMode.Update, FileText = file, TakeOver = true });

    planned.Result.Should().Be(ManagedUpdateResults.Planned, ManagedTestRig.Describe(planned));
    planned.TakenOver.Should().Be(1);
    planned.TakeOvers.Should().ContainSingle().Which.Name.Should().Be("alpha");
    planned.TakeOverBackupId.Should().BeNull();
    done.Result.Should().Be(ManagedUpdateResults.Done, ManagedTestRig.Describe(done));
    done.TakenOver.Should().Be(1);
    done.TakeOverBackupId.Should().MatchRegex("^to-[0-9]{8}T[0-9]{9}-[0-9a-f]{8}$");
    done.TakeOvers.Should().BeEmpty();
  }
}
