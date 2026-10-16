using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using GameBot.Domain.Gamepacks.Managed;
using Xunit;

#pragma warning disable CA2007, CA1707, CA1861, CA1305, CA1307

namespace GameBot.UnitTests.Gamepacks.Managed;

/// <summary>Feature 148 (T021): the happy path of the managed update, with real stores and a fake queue port.</summary>
public sealed class GamepackUpdateOrchestratorTests {
  [Fact]
  public async Task AnUpdateStopsBacksUpInstallsBooksAndRestartsOnlyTheStoppedQueues() {
    using var rig = new ManagedTestRig();
    await rig.InstallAsync("1.0.0", "alpha", "beta");
    var daily = await rig.AddTemplateAsync("daily", "alpha", "beta");
    var other = await rig.AddTemplateAsync("other", "alpha");
    var running = await rig.AddQueueAsync("Running", daily, running: true);
    var parked = await rig.AddQueueAsync("Parked", other, running: false);
    var report = await rig.RunAsync(ManagedTestRig.Update(ManagedTestRig.Pack("2.0.0", "alpha", "beta", "gamma")));

    report.Result.Should().Be(ManagedUpdateResults.Done, ManagedTestRig.Describe(report));
    report.Reason.Should().BeNull();
    report.GamepackName.Should().Be("pns");
    report.GamepackVersion.Should().Be("2.0.0");
    report.PreviousVersion.Should().Be("1.0.0");
    report.StoppedQueues.Select(q => q.Id).Should().Equal(running.Id);
    report.RestartedQueues.Select(q => q.Id).Should().Equal(running.Id);
    report.NotRestartedQueues.Should().BeEmpty();
    report.BackupId.Should().NotBeNull();
    rig.Backups.Read(report.BackupId!).Should().Contain("daily");
    report.Created.Should().Be(1);

    // Only the queue that the flow stopped is started. The parked queue is not started.
    rig.Control.StopCalls.Should().Equal(running.Id);
    rig.Control.StartCalls.Should().Equal(running.Id);
    rig.Control.Running.Should().Contain(running.Id).And.NotContain(parked.Id);

    // The new sequence is booked, disabled, in both affected templates.
    report.NewEntries.Select(e => e.TemplateName).Should().BeEquivalentTo(new[] { "daily", "other" });
    var dailyAfter = await rig.TemplateAsync("daily");
    dailyAfter.Entries.Should().HaveCount(3);
    dailyAfter.Entries[2].Enabled.Should().BeFalse();
    (await rig.TemplateAsync("other")).Entries[1].Enabled.Should().BeFalse();
    rig.Service.List().Single().Version.Should().Be("2.0.0");
  }

  [Fact]
  public async Task AQueueThatTheUserStoppedIsNotStartedButItsTemplateGetsTheNewEntries() {
    using var rig = new ManagedTestRig();
    await rig.InstallAsync("1.0.0", "alpha");
    var daily = await rig.AddTemplateAsync("daily", "alpha");
    await rig.AddQueueAsync("Parked", daily, running: false);

    var report = await rig.RunAsync(ManagedTestRig.Update(ManagedTestRig.Pack("2.0.0", "alpha", "gamma")));

    report.Result.Should().Be(ManagedUpdateResults.Done, ManagedTestRig.Describe(report));
    report.StoppedQueues.Should().BeEmpty();
    rig.Control.Calls.Should().BeEmpty();
    report.NewEntries.Should().ContainSingle(e => e.TemplateName == "daily" && e.SequenceName == "gamma");
    (await rig.TemplateAsync("daily")).Entries.Last().Enabled.Should().BeFalse();
  }

  [Fact]
  public async Task NoBackupIsWrittenWhenTheUpdateAffectsNoTemplate() {
    using var rig = new ManagedTestRig();
    await rig.InstallAsync("1.0.0", "alpha");
    await rig.AddTemplateAsync("unrelated");

    var report = await rig.RunAsync(ManagedTestRig.Update(ManagedTestRig.Pack("2.0.0", "alpha")));

    report.Result.Should().Be(ManagedUpdateResults.Done, ManagedTestRig.Describe(report));
    report.BackupId.Should().BeNull();
    report.NewEntries.Should().BeEmpty();
    rig.Service.List().Single().Version.Should().Be("2.0.0");
  }

  [Fact]
  public async Task AFirstInstallStopsNoQueueAndBooksNoEntry() {
    using var rig = new ManagedTestRig();
    var template = await rig.BaseTemplates.CreateAsync(new GameBot.Domain.QueueTemplates.QueueTemplate { Id = "t1", Name = "empty" });
    await rig.AddQueueAsync("Q", template, running: true);

    var report = await rig.RunAsync(ManagedTestRig.Update(ManagedTestRig.Pack("1.0.0", "alpha")));

    report.Result.Should().Be(ManagedUpdateResults.Done, ManagedTestRig.Describe(report));
    report.PreviousVersion.Should().BeNull();
    report.StoppedQueues.Should().BeEmpty();
    report.BackupId.Should().BeNull();
    rig.Control.Calls.Should().BeEmpty();
    rig.Service.List().Single().Name.Should().Be("pns");
  }

  [Fact]
  public async Task AQueueThatDoesNotStartIsListedAndTheResultStaysDone() {
    using var rig = new ManagedTestRig();
    await rig.InstallAsync("1.0.0", "alpha");
    var daily = await rig.AddTemplateAsync("daily", "alpha");
    var first = await rig.AddQueueAsync("First", daily, running: true);
    var second = await rig.AddQueueAsync("Second", daily, running: true);
    rig.Control.StartFails.Add(first.Id);

    var report = await rig.RunAsync(ManagedTestRig.Update(ManagedTestRig.Pack("2.0.0", "alpha", "gamma")));

    report.Result.Should().Be(ManagedUpdateResults.Done, ManagedTestRig.Describe(report));
    report.NotRestartedQueues.Select(q => q.Id).Should().Equal(first.Id);
    report.NotRestartedQueues[0].Error.Should().NotBeNullOrWhiteSpace();
    report.RestartedQueues.Select(q => q.Id).Should().Equal(second.Id);
  }

  [Fact]
  public async Task TheTemplateListLimitsTheStopsAndTheEntryChanges() {
    using var rig = new ManagedTestRig();
    await rig.InstallAsync("1.0.0", "alpha");
    var one = await rig.AddTemplateAsync("one", "alpha");
    var two = await rig.AddTemplateAsync("two", "alpha");
    var queueOne = await rig.AddQueueAsync("Q1", one, running: true);
    var queueTwo = await rig.AddQueueAsync("Q2", two, running: true);

    var report = await rig.RunAsync(ManagedTestRig.Update(ManagedTestRig.Pack("2.0.0", "alpha", "gamma"), templates: "ONE"));

    report.Result.Should().Be(ManagedUpdateResults.Done, ManagedTestRig.Describe(report));
    report.StoppedQueues.Select(q => q.Id).Should().Equal(queueOne.Id);
    rig.Control.StopCalls.Should().NotContain(queueTwo.Id);
    (await rig.TemplateAsync("one")).Entries.Should().HaveCount(2);
    (await rig.TemplateAsync("two")).Entries.Should().HaveCount(1);
  }
}
