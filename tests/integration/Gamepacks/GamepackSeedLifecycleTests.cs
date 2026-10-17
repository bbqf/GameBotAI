using System;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using GameBot.Domain.Gamepacks;
using GameBot.UnitTests.Gamepacks;
using Xunit;

#pragma warning disable CA2007, CA1861, CA1707, CA1416

namespace GameBot.IntegrationTests.Gamepacks;

/// <summary>Feature 150 (T023): the seed lifecycle on real file stores.</summary>
public sealed class GamepackSeedLifecycleTests : IDisposable {
  private readonly GamepackTestKit _kit = new();

  public void Dispose() => _kit.Dispose();

  private static string Pack(string version, params object[] seeds) => GamepackTestKit.Pack("pns", version,
    sequences: new[] { GamepackTestKit.Seq("a1", "Help All") },
    seedTemplates: seeds.Length == 0 ? null : seeds);

  private static object Seed(string name = "Daily") => GamepackTestKit.Seed(name, GamepackTestKit.SeedEntry("Help All"));

  [Fact]
  public async Task FirstInstallLaterInstallAndDeleteThenInstall() {
    var first = await _kit.Service.InstallAsync(Pack("1", Seed()), dryRun: false);
    first.Status.Should().Be(GamepackInstallStatus.Ok);
    first.SeedTemplatesCreated.Should().Equal("Daily");
    var template = (await _kit.Templates.ListAsync()).Single();

    var second = await _kit.Service.InstallAsync(Pack("2", Seed(), Seed("Other")), dryRun: false);
    second.Status.Should().Be(GamepackInstallStatus.Ok);
    second.SeedTemplatesCreated.Should().BeEmpty();
    second.SeedTemplatesSkipped.Should().BeEmpty();
    (await _kit.Templates.ListAsync()).Select(t => t.Id).Should().Equal(template.Id);

    (await _kit.Templates.DeleteAsync(template.Id)).Should().BeTrue();
    var third = await _kit.Service.InstallAsync(Pack("3", Seed()), dryRun: false);
    third.Status.Should().Be(GamepackInstallStatus.Ok);
    third.SeedTemplatesCreated.Should().BeEmpty();
    (await _kit.Templates.ListAsync()).Should().BeEmpty();
  }

  [Fact]
  public async Task AFaultAtEveryWriteOfAFirstInstallLeavesNoSeedTemplate() {
    _kit.Gate.Arm(0);
    (await _kit.Service.InstallAsync(Pack("1", Seed(), Seed("Other")), dryRun: false)).Status.Should().Be(GamepackInstallStatus.Ok);
    var total = _kit.Gate.Calls;
    total.Should().BeGreaterThan(3, "one sequence, two seeds, and the registry");

    for (var n = 1; n <= total; n++) {
      using var kit = new GamepackTestKit();
      var before = kit.Snapshot();

      kit.Gate.Arm(n);
      var outcome = await kit.Service.InstallAsync(Pack("1", Seed(), Seed("Other")), dryRun: false);

      outcome.Status.Should().Be(GamepackInstallStatus.Failed, "a fault at write call " + n);
      (await kit.Templates.ListAsync()).Should().BeEmpty("a fault at call " + n);
      (await kit.Sequences.ListAsync()).Should().BeEmpty();
      kit.Store.Load().Gamepacks.Should().BeEmpty();
      kit.Snapshot().Keys.Where(k => !k.Contains("queue-templates", StringComparison.OrdinalIgnoreCase))
        .Should().BeEquivalentTo(before.Keys.Where(k => !k.Contains("queue-templates", StringComparison.OrdinalIgnoreCase)));
    }
  }
}
