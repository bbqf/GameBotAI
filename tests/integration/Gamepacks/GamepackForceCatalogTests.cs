using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using FluentAssertions;
using GameBot.Domain.Gamepacks;
using GameBot.Domain.Gamepacks.Catalog;
using GameBot.Domain.Gamepacks.Managed;
using GameBot.UnitTests.Gamepacks;
using GameBot.UnitTests.Gamepacks.Managed;
using Xunit;

#pragma warning disable CA2007, CA1861, CA1707, CA2000, CA1305, CA1307, CA1310, CA1849

namespace GameBot.IntegrationTests.Gamepacks;

/// <summary>Feature 178 (US4): the force option on the catalog install and on the managed update, with real stores.</summary>
public sealed class GamepackForceCatalogTests {
  [Fact]
  public async Task ACatalogInstallWithForceInstallsTheSameVersionAgain() {
    using var rig = new CatalogRig();
    new CatalogFixture().Add("pns-daily", "1.0.0", CatalogFixture.SimplePack("pns-daily", "1.0.0")).WriteTo(rig.CatalogFolder);
    rig.Settings.Save(rig.CatalogFolder, false, null);
    (await rig.Service.InstallAsync("pns-daily", "1.0.0", dryRun: false)).Status.Should().Be(GamepackInstallStatus.Ok);

    var plain = await rig.Service.InstallAsync("pns-daily", "1.0.0", new GamepackInstallOptions());
    plain.Reinstalled.Should().BeFalse();
    rig.Kit.Store.ReadReinstall("pns-daily").Should().BeNull();

    var forced = await rig.Service.InstallAsync("pns-daily", "1.0.0", new GamepackInstallOptions(Force: true));

    forced.Status.Should().Be(GamepackInstallStatus.Ok);
    forced.Reinstalled.Should().BeTrue();
    forced.Overwritten.Should().NotBeNull();
    rig.Kit.Store.ReadReinstall("pns-daily").Should().NotBeNull();
  }

  [Fact]
  public async Task ACatalogInstallDryRunWithForceChangesNothing() {
    using var rig = new CatalogRig();
    new CatalogFixture().Add("pns-daily", "1.0.0", CatalogFixture.SimplePack("pns-daily", "1.0.0")).WriteTo(rig.CatalogFolder);
    rig.Settings.Save(rig.CatalogFolder, false, null);
    await rig.Service.InstallAsync("pns-daily", "1.0.0", dryRun: false);
    var before = rig.Kit.Snapshot();

    var dry = await rig.Service.InstallAsync("pns-daily", "1.0.0", new GamepackInstallOptions(DryRun: true, Force: true));

    dry.Reinstalled.Should().BeTrue();
    rig.Kit.Snapshot().Should().BeEquivalentTo(before);
  }

  private static async Task<ManagedTestRig> ArrangeAsync() {
    var rig = new ManagedTestRig();
    await rig.InstallAsync("1.0.0", "alpha", "beta");
    var template = await rig.AddTemplateAsync("daily", "alpha", "beta");
    await rig.AddQueueAsync("Q", template, running: true);
    return rig;
  }

  private static ManagedUpdateRequest Request(string version, bool force, bool dryRun = false) => new() {
    Mode = ManagedUpdateMode.Update,
    FileText = ManagedTestRig.Pack(version, "alpha", "beta"),
    DryRun = dryRun,
    Force = force
  };

  [Fact]
  public async Task AManagedUpdateOfTheSameVersionWithoutForceDoesNothing() {
    using var rig = await ArrangeAsync();

    var report = await rig.RunAsync(Request("1.0.0", force: false));

    report.Result.Should().Be(ManagedUpdateResults.Done);
    report.Reinstalled.Should().BeFalse();
    report.Overwritten.Should().BeNull();
    report.BackupId.Should().BeNull();
    rig.Packs.Store.ReadReinstall("pns").Should().BeNull();
  }

  [Fact]
  public async Task AManagedUpdateOfTheSameVersionWithForceRunsTheFullFlow() {
    using var rig = await ArrangeAsync();

    var report = await rig.RunAsync(Request("1.0.0", force: true));

    report.Result.Should().Be(ManagedUpdateResults.Done, ManagedTestRig.Describe(report));
    report.Reinstalled.Should().BeTrue();
    report.Overwritten.Should().NotBeNull();
    report.BackupId.Should().NotBeNull("the flow backs up the affected templates");
    report.StoppedQueues.Should().HaveCount(1);
    report.RestartedQueues.Should().HaveCount(1);
    rig.Packs.Store.ReadReinstall("pns").Should().NotBeNull();
    rig.Service.List().Single().HasReinstallPoint.Should().BeTrue();
  }

  [Fact]
  public async Task AManagedDryRunWithForceReportsTheListAndChangesNothing() {
    using var rig = await ArrangeAsync();
    var before = rig.Packs.Snapshot();

    var report = await rig.RunAsync(Request("1.0.0", force: true, dryRun: true));

    report.Result.Should().Be(ManagedUpdateResults.Planned, ManagedTestRig.Describe(report));
    report.Reinstalled.Should().BeTrue();
    report.Overwritten.Should().NotBeNull();
    rig.Packs.Snapshot().Should().BeEquivalentTo(before);
  }

  [Fact]
  public async Task AManagedRollbackUsesTheReinstallPoint() {
    using var rig = await ArrangeAsync();
    await rig.RunAsync(Request("1.0.0", force: true));

    var dry = await rig.RunAsync(ManagedTestRig.Rollback("pns", dryRun: true));
    var report = await rig.RunAsync(ManagedTestRig.Rollback("pns"));

    dry.Result.Should().Be(ManagedUpdateResults.Planned, ManagedTestRig.Describe(dry));
    report.Result.Should().Be(ManagedUpdateResults.Done, ManagedTestRig.Describe(report));
    rig.Packs.Store.ReadReinstall("pns").Should().BeNull();
    rig.Service.List().Single().Version.Should().Be("1.0.0");
  }

  [Fact]
  public async Task AManagedRollbackWithNeitherPointIsRejected() {
    using var rig = await ArrangeAsync();

    var report = await rig.RunAsync(ManagedTestRig.Rollback("pns"));

    report.Result.Should().Be(ManagedUpdateResults.Rejected);
    report.Reason.Should().Be(ManagedUpdateReasons.NoPreviousVersion);
  }

  [Fact]
  public async Task AManagedCatalogUpdateWithForceInstallsTheEqualVersionAgain() {
    using var rig = await ArrangeAsync();
    var folder = Path.Combine(rig.Root, "catalog");
    new CatalogFixture().Add("pns", "1.0.0", ManagedTestRig.Pack("1.0.0", "alpha", "beta")).WriteTo(folder);
    var settings = new GamepackCatalogSettingsStore(rig.Root);
    settings.Save(folder, false, null);
    var options = new CatalogOptions();
    var catalog = new GamepackCatalogService(new GamepackCatalogSourceFactory(settings, () => new HttpClient(), options), rig.Service, options);
    var fetched = await catalog.FetchAsync("pns", "1.0.0");

    var report = await rig.RunAsync(new ManagedUpdateRequest { Mode = ManagedUpdateMode.Update, FileText = fetched.FileText, SignatureText = fetched.SignatureText, Force = true });

    report.Result.Should().Be(ManagedUpdateResults.Done, ManagedTestRig.Describe(report));
    report.Reinstalled.Should().BeTrue();
  }

  [Fact]
  public async Task AManagedUpdateOfADifferentVersionWithForceIsAnUpdate() {
    using var rig = await ArrangeAsync();

    var report = await rig.RunAsync(Request("2.0.0", force: true));

    report.Result.Should().Be(ManagedUpdateResults.Done, ManagedTestRig.Describe(report));
    report.Reinstalled.Should().BeFalse();
    rig.Service.List().Single().Version.Should().Be("2.0.0");
    rig.Packs.Store.ReadReinstall("pns").Should().BeNull();
  }
}
