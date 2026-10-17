using System.Net;
using System.Threading.Tasks;
using FluentAssertions;
using GameBot.UnitTests.Gamepacks;
using Xunit;

#pragma warning disable CA2007, CA1861, CA1707, CA2000, CA1305, CA1307, CA1310, CA1849

namespace GameBot.ContractTests.Gamepacks;

/// <summary>Feature 150 (T029): the dry run shows the seeds and changes nothing.</summary>
public sealed class GamepackSeedDryRunContractTests {
  [Fact]
  public async Task ADryRunListsWhatTheRealInstallDoesAndCreatesNothing() {
    using var host = new GamepackContractHost();
    (await host.InstallAsync(GamepackTestKit.Pack("base", "1", sequences: new[] { GamepackTestKit.Seq("b1", "Base Seq") }))).EnsureSuccessStatusCode();
    await GamepackSeedContractKit.MakeTemplateAsync(host, "Used Name", "Base Seq");
    var pack = GamepackSeedContractKit.Pack("1.0.0", seeds: new[] { GamepackSeedContractKit.Daily(), GamepackTestKit.Seed("used name", GamepackTestKit.SeedEntry("Help All")) });
    var before = await GamepackSeedContractKit.TemplatesAsync(host);

    var dry = await host.InstallAsync(pack, dryRun: true);

    dry.StatusCode.Should().Be(HttpStatusCode.OK);
    var dryBody = await GamepackContractHost.ReadAsync(dry);
    GamepackSeedContractKit.Names(dryBody.GetProperty("seedTemplatesCreated")).Should().Equal("Daily Help");
    GamepackSeedContractKit.Skips(dryBody.GetProperty("seedTemplatesSkipped")).Should().Equal(("used name", "skipped_exists"));
    (await GamepackSeedContractKit.TemplatesAsync(host)).Should().BeEquivalentTo(before);

    var real = await host.InstallAsync(pack);

    var realBody = await GamepackContractHost.ReadAsync(real);
    GamepackSeedContractKit.Names(realBody.GetProperty("seedTemplatesCreated")).Should().Equal(GamepackSeedContractKit.Names(dryBody.GetProperty("seedTemplatesCreated")));
    GamepackSeedContractKit.Skips(realBody.GetProperty("seedTemplatesSkipped")).Should().Equal(GamepackSeedContractKit.Skips(dryBody.GetProperty("seedTemplatesSkipped")));
  }

  [Fact]
  public async Task ASeedThatNamesAMissingSequenceGivesAReferenceProblem() {
    using var host = new GamepackContractHost();
    var pack = GamepackSeedContractKit.Pack("1.0.0", seeds: GamepackTestKit.Seed("Broken", GamepackTestKit.SeedEntry("No Such Sequence")));
    var state = await host.StateSnapshotAsync();

    var dry = await host.InstallAsync(pack, dryRun: true);

    dry.StatusCode.Should().Be(HttpStatusCode.OK);
    var dryBody = await GamepackContractHost.ReadAsync(dry);
    dryBody.GetProperty("valid").GetBoolean().Should().BeFalse();
    GamepackContractHost.ProblemCategories(dryBody).Should().Contain("reference");
    dryBody.GetProperty("problems")[0].GetProperty("objectType").GetString().Should().Be("seedTemplate");

    var real = await host.InstallAsync(pack);

    real.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    (await host.StateSnapshotAsync()).Should().Be(state);
    (await GamepackSeedContractKit.TemplatesAsync(host)).Should().BeEmpty();
  }

  [Fact]
  public async Task ADryRunAfterAnEarlierInstallShowsNoSeed() {
    using var host = new GamepackContractHost();
    (await host.InstallAsync(GamepackSeedContractKit.Pack("1.0.0", seeds: GamepackSeedContractKit.Daily()))).EnsureSuccessStatusCode();

    var dry = await host.InstallAsync(GamepackSeedContractKit.Pack("2.0.0", seeds: GamepackSeedContractKit.Daily()), dryRun: true);

    var body = await GamepackContractHost.ReadAsync(dry);
    body.GetProperty("seedTemplatesCreated").GetArrayLength().Should().Be(0);
    body.GetProperty("seedTemplatesSkipped").GetArrayLength().Should().Be(0);
  }
}
