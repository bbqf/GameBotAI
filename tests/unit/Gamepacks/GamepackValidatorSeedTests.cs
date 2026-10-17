using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using GameBot.Domain.Gamepacks;
using GameBot.Domain.Gamepacks.Seeds;
using Xunit;

#pragma warning disable CA1707, CA2007, CA1861

namespace GameBot.UnitTests.Gamepacks;

/// <summary>Feature 150 (T013): the validator plans the seeds of a first install.</summary>
public sealed class GamepackValidatorSeedTests {
  internal static string Pack(params object[] seeds) => GamepackTestKit.Pack("p", "1",
    sequences: new[] { GamepackTestKit.Seq("a1", "Help All") },
    seedTemplates: seeds);

  private static async Task<GamepackValidationResult> CheckAsync(GamepackTestKit kit, string json) {
    var parsed = GamepackFileSerializer.Parse(json);
    return await kit.Validator.ValidateAsync(parsed.File!, parsed.Problems, seeds: true);
  }

  [Fact]
  public async Task AFreeNameIsPlannedAsCreateWithTheFinalSequenceId() {
    using var kit = new GamepackTestKit();

    var result = await CheckAsync(kit, Pack(GamepackTestKit.Seed("Daily", GamepackTestKit.SeedEntry("Help All"))));

    result.IsValid.Should().BeTrue(string.Join(";", result.Problems.Select(p => p.Message)));
    var item = result.Plan!.Seeds.Items.Single();
    item.Outcome.Should().Be(SeedOutcomes.Create);
    item.TemplateId.Should().NotBeNullOrWhiteSpace();
    item.Entries.Single().SequenceId.Should().Be(result.Plan.Writes.Single(w => w.Name == "Help All").FinalId);
    result.Plan.SeedsToCreate.Should().Be(1);
    result.Plan.SeedsSkipped.Should().Be(0);
  }

  [Fact]
  public async Task AUsedNameIsPlannedAsSkipWithoutCase() {
    using var kit = new GamepackTestKit();
    await kit.AddTemplateAsync("DAILY");

    var result = await CheckAsync(kit, Pack(GamepackTestKit.Seed("daily", GamepackTestKit.SeedEntry("Help All"))));

    var item = result.Plan!.Seeds.Items.Single();
    item.Outcome.Should().Be(SeedOutcomes.SkipExists);
    item.Entries.Should().BeEmpty();
    result.Plan.SeedsSkipped.Should().Be(1);
  }

  [Fact]
  public async Task AnEntryCanNameASequenceThatIsOnTheInstance() {
    using var kit = new GamepackTestKit();
    await kit.AddUserObjectsAsync(GamepackTestKit.Pack("x", "1", sequences: new[] { GamepackTestKit.Seq("u1", "On Instance") }));
    var existingId = (await kit.SequenceByNameAsync("On Instance"))!.Id;
    var json = GamepackTestKit.Pack("p", "1", seedTemplates: new[] { GamepackTestKit.Seed("Daily", GamepackTestKit.SeedEntry("on instance")) });

    var result = await CheckAsync(kit, json);

    result.IsValid.Should().BeTrue();
    result.Plan!.Seeds.Items.Single().Entries.Single().SequenceId.Should().Be(existingId);
  }

  [Fact]
  public async Task AMissingSequenceGivesAReferenceProblem() {
    using var kit = new GamepackTestKit();

    var result = await CheckAsync(kit, Pack(GamepackTestKit.Seed("Daily", GamepackTestKit.SeedEntry("Nothing Here"))));

    result.IsValid.Should().BeFalse();
    result.Problems.Should().ContainSingle(p => p.Category == GamepackProblemCategories.Reference && p.ObjectType == GamepackObjectTypes.SeedTemplate && p.Name == "Daily");
  }

  [Fact]
  public async Task AnInvalidSeedGivesAStructureProblem() {
    using var kit = new GamepackTestKit();

    var result = await CheckAsync(kit, Pack(GamepackTestKit.Seed("bad/name", GamepackTestKit.SeedEntry("Help All"))));

    result.Problems.Should().Contain(p => p.Category == GamepackProblemCategories.Structure && p.ObjectType == GamepackObjectTypes.SeedTemplate);
  }

  [Fact]
  public async Task TwoSeedsWithOneNameGiveAStructureProblem() {
    using var kit = new GamepackTestKit();

    var result = await CheckAsync(kit, Pack(
      GamepackTestKit.Seed("Daily", GamepackTestKit.SeedEntry("Help All")),
      GamepackTestKit.Seed("DAILY", GamepackTestKit.SeedEntry("Help All"))));

    result.Problems.Should().ContainSingle(p => p.Category == GamepackProblemCategories.Structure && p.Message.Contains("two seed templates"));
  }

  [Fact]
  public async Task AFileWithoutSeedsGivesAnEmptySeedPlan() {
    using var kit = new GamepackTestKit();

    var result = await CheckAsync(kit, GamepackTestKit.Pack("p", "1", sequences: new[] { GamepackTestKit.Seq("a1", "Help All") }));

    result.Plan!.Seeds.Items.Should().BeEmpty();
  }
}
