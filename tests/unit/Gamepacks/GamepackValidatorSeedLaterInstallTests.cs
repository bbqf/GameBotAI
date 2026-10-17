using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using GameBot.Domain.Gamepacks;
using Xunit;

#pragma warning disable CA1707, CA2007, CA1861

namespace GameBot.UnitTests.Gamepacks;

/// <summary>Feature 150 (T024, FR-015): a later install checks the seed structure only. It plans no seed.</summary>
public sealed class GamepackValidatorSeedLaterInstallTests {
  private static async Task<GamepackTestKit> InstalledAsync() {
    var kit = new GamepackTestKit();
    (await kit.Service.InstallAsync(GamepackTestKit.Pack("p", "1", sequences: new[] { GamepackTestKit.Seq("a1", "Help All") }), false)).Status.Should().Be(GamepackInstallStatus.Ok);
    return kit;
  }

  private static async Task<GamepackValidationResult> CheckAsync(GamepackTestKit kit, string json, bool seeds = true) {
    var parsed = GamepackFileSerializer.Parse(json);
    return await kit.Validator.ValidateAsync(parsed.File!, parsed.Problems, seeds: seeds);
  }

  [Fact]
  public async Task ABadSeedStructureStillGivesAProblemWhenARecordExists() {
    using var kit = await InstalledAsync();
    var json = GamepackTestKit.Pack("p", "2", sequences: new[] { GamepackTestKit.Seq("a1", "Help All") },
      seedTemplates: new[] { GamepackTestKit.Seed("bad/name", GamepackTestKit.SeedEntry("Help All")) });

    var result = await CheckAsync(kit, json);

    result.Problems.Should().ContainSingle(p => p.Category == GamepackProblemCategories.Structure && p.ObjectType == GamepackObjectTypes.SeedTemplate);
  }

  [Fact]
  public async Task AMissingSequenceGivesNoReferenceProblemAndNoSeedPlan() {
    using var kit = await InstalledAsync();
    var json = GamepackTestKit.Pack("p", "2", sequences: new[] { GamepackTestKit.Seq("a1", "Help All") },
      seedTemplates: new[] { GamepackTestKit.Seed("Daily", GamepackTestKit.SeedEntry("Nothing Here")) });

    var result = await CheckAsync(kit, json);

    result.IsValid.Should().BeTrue(string.Join(";", result.Problems.Select(p => p.Message)));
    result.Plan!.Seeds.Items.Should().BeEmpty();
  }

  [Fact]
  public async Task ARollbackStyleCheckRunsNoSeedCheckAndPlansNoSeed() {
    using var kit = new GamepackTestKit();
    var json = GamepackTestKit.Pack("p", "1", sequences: new[] { GamepackTestKit.Seq("a1", "Help All") },
      seedTemplates: new[] { GamepackTestKit.Seed("bad/name", GamepackTestKit.SeedEntry("Nothing Here")) });

    var result = await CheckAsync(kit, json, seeds: false);

    result.IsValid.Should().BeTrue();
    result.Plan!.Seeds.Items.Should().BeEmpty();
  }
}
