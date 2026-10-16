using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using GameBot.Domain.Gamepacks.Managed;
using Xunit;

#pragma warning disable CA2007, CA1707, CA1861

namespace GameBot.UnitTests.Gamepacks.Managed;

/// <summary>Feature 148 (T042): a missing template name gives the same reason for a dry run and a normal call.</summary>
public sealed class GamepackUpdateOrchestratorDryRunScopeTests {
  [Theory]
  [InlineData(true)]
  [InlineData(false)]
  public async Task AMissingTemplateGivesTemplateNotFoundBeforeAnyChange(bool dryRun) {
    using var rig = new ManagedTestRig();
    await rig.InstallAsync("1.0.0", "alpha");
    var template = await rig.AddTemplateAsync("daily", "alpha");
    await rig.AddQueueAsync("Q", template, running: true);
    var before = await rig.StateAsync();

    var report = await rig.RunAsync(ManagedTestRig.Update(ManagedTestRig.Pack("2.0.0", "alpha"), dryRun, null, "daily", "nope"));

    report.Result.Should().Be(ManagedUpdateResults.Rejected);
    report.Reason.Should().Be(ManagedUpdateReasons.TemplateNotFound);
    report.Problems.Should().Contain(p => p.Code == ManagedUpdateReasons.TemplateNotFound && p.Name == "nope");
    rig.Control.Calls.Should().BeEmpty();
    (await rig.StateAsync()).Should().Be(before);
  }

  [Fact]
  public async Task AListedTemplateThatTheGamepackDoesNotAffectIsReportedAsNotAffected() {
    using var rig = new ManagedTestRig();
    await rig.InstallAsync("1.0.0", "alpha");
    await rig.AddTemplateAsync("daily", "alpha");
    await rig.BaseTemplates.CreateAsync(new GameBot.Domain.QueueTemplates.QueueTemplate { Id = "plain", Name = "plain" });

    var report = await rig.RunAsync(ManagedTestRig.Update(ManagedTestRig.Pack("2.0.0", "alpha"), true, null, "plain"));

    report.Result.Should().Be(ManagedUpdateResults.Planned, ManagedTestRig.Describe(report));
    report.NotAffectedTemplates.Should().Equal("plain");
    report.StoppedQueues.Should().BeEmpty();
  }

  [Fact]
  public async Task AMissingTemplateWinsOverASignatureProblemAndTheReportKeepsBoth() {
    using var rig = new ManagedTestRig();
    rig.SignatureSettings.SetPolicy(GameBot.Domain.Gamepacks.Signing.SignaturePolicy.Required);
    await rig.BaseTemplates.CreateAsync(new GameBot.Domain.QueueTemplates.QueueTemplate { Id = "t", Name = "t" });

    var report = await rig.RunAsync(ManagedTestRig.Update(ManagedTestRig.Pack("1.0.0", "alpha"), true, null, "nope"));

    report.Reason.Should().Be(ManagedUpdateReasons.TemplateNotFound);
    report.Problems.Select(p => p.Category).Should().Contain(new[] { "signature", "structure" });
    report.Signature!.WouldReject.Should().BeTrue();
  }
}
