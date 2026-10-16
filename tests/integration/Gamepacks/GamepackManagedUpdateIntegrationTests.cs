using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using GameBot.Domain.Gamepacks.Catalog;
using GameBot.Domain.Gamepacks.Managed;
using GameBot.Domain.Gamepacks.Signing;
using GameBot.UnitTests.Gamepacks;
using GameBot.UnitTests.Gamepacks.Managed;
using Xunit;

#pragma warning disable CA2007, CA1861, CA1707, CA2000, CA1305, CA1307, CA1310, CA1849

namespace GameBot.IntegrationTests.Gamepacks;

/// <summary>Feature 148 (T026): the managed update with real stores in a temporary data root and a fake queue port.</summary>
public sealed class GamepackManagedUpdateIntegrationTests {
  private static async Task<ManagedTestRig> ArrangeAsync() {
    var rig = new ManagedTestRig();
    await rig.InstallAsync("1.0.0", "alpha", "beta");
    var template = await rig.AddTemplateAsync("daily", "alpha", "beta");
    await rig.AddQueueAsync("Q", template, running: true);
    return rig;
  }

  [Fact]
  public async Task AFileUpdateDoesTheWholeFlow() {
    using var rig = await ArrangeAsync();

    var report = await rig.RunAsync(ManagedTestRig.Update(ManagedTestRig.Pack("2.0.0", "alpha", "beta", "gamma")));

    report.Result.Should().Be(ManagedUpdateResults.Done, ManagedTestRig.Describe(report));
    rig.Service.List().Single().Version.Should().Be("2.0.0");
    rig.Backups.Read(report.BackupId!).Should().Contain("daily");
    rig.Control.Running.Should().HaveCount(1);
    (await rig.TemplateAsync("daily")).Entries.Should().HaveCount(3);
  }

  [Fact]
  public async Task ACatalogUpdateUsesTheFileOfTheFolderCatalog() {
    using var rig = await ArrangeAsync();
    var folder = Path.Combine(rig.Root, "catalog");
    new CatalogFixture().Add("pns", "2.0.0", ManagedTestRig.Pack("2.0.0", "alpha", "beta", "gamma")).WriteTo(folder);
    var settings = new GamepackCatalogSettingsStore(rig.Root);
    settings.Save(folder, false, null);
    var options = new CatalogOptions();
    var catalog = new GamepackCatalogService(
      new GamepackCatalogSourceFactory(settings, () => new HttpClient(), options), rig.Service, options);

    var fetched = await catalog.FetchAsync("pns", "2.0.0");
    var report = await rig.RunAsync(ManagedTestRig.Update(fetched.FileText, signature: fetched.SignatureText));

    report.Result.Should().Be(ManagedUpdateResults.Done, ManagedTestRig.Describe(report));
    rig.Service.List().Single().Version.Should().Be("2.0.0");
  }

  [Fact]
  public async Task ARejectedSignatureChangesNothingAndStopsNoQueue() {
    using var rig = await ArrangeAsync();
    rig.SignatureSettings.SetPolicy(SignaturePolicy.Required);
    var before = rig.Packs.Snapshot();

    var report = await rig.RunAsync(ManagedTestRig.Update(ManagedTestRig.Pack("2.0.0", "alpha", "gamma")));

    report.Result.Should().Be(ManagedUpdateResults.Rejected);
    report.Reason.Should().Be(ManagedUpdateReasons.SignatureRejected);
    rig.Control.Calls.Should().BeEmpty();
    rig.Packs.Snapshot().Should().Equal(before);
  }

  [Fact]
  public async Task ATemplateListLimitsTheChanges() {
    using var rig = await ArrangeAsync();
    var other = await rig.AddTemplateAsync("other", "alpha");
    await rig.AddQueueAsync("Q2", other, running: true);

    var report = await rig.RunAsync(ManagedTestRig.Update(ManagedTestRig.Pack("2.0.0", "alpha", "beta", "gamma"), templates: "other"));

    report.Result.Should().Be(ManagedUpdateResults.Done, ManagedTestRig.Describe(report));
    report.StoppedQueues.Should().ContainSingle().Which.TemplateName.Should().Be("other");
    (await rig.TemplateAsync("daily")).Entries.Should().HaveCount(2);
    (await rig.TemplateAsync("other")).Entries.Should().HaveCount(2);
    report.NotAffectedTemplates.Should().BeEmpty();
  }

  [Fact]
  public async Task TwoParallelCallsForTwoGamepacksGiveOneConflict() {
    using var rig = await ArrangeAsync();
    var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    rig.Control.OnStop = async _ => {
      entered.TrySetResult();
      await release.Task;
    };

    var first = Task.Run(() => rig.RunAsync(ManagedTestRig.Update(ManagedTestRig.Pack("2.0.0", "alpha", "gamma"))));
    await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
    var second = await rig.RunAsync(ManagedTestRig.Update(ManagedTestRig.PackNamed("other", "1.0.0", "zeta")));
    release.SetResult();
    var firstReport = await first;

    firstReport.Result.Should().Be(ManagedUpdateResults.Done, ManagedTestRig.Describe(firstReport));
    second.Reason.Should().Be(ManagedUpdateReasons.UpdateInProgress);
    rig.Service.List().Select(g => g.Name).Should().Equal("pns");
  }
}
