using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using GameBot.Domain.Gamepacks;
using GameBot.Domain.Gamepacks.Seeds;
using Xunit;

#pragma warning disable CA1707, CA2007, CA1861

namespace GameBot.UnitTests.Gamepacks;

/// <summary>Feature 150 (T015, FR-016): a name that is used between plan time and write time gives no overwrite and no duplicate.</summary>
public sealed class GamepackApplierSeedRaceTests {
  [Fact]
  public async Task ANameUsedAfterThePlanIsSkippedAtWriteTime() {
    using var kit = new GamepackTestKit();
    var parsed = GamepackFileSerializer.Parse(GamepackApplierSeedTests.Pack(
      GamepackTestKit.Seed("Daily", GamepackTestKit.SeedEntry("Help All")),
      GamepackTestKit.Seed("Weekly", GamepackTestKit.SeedEntry("Help All"))));
    var plan = (await kit.Validator.ValidateAsync(parsed.File!, parsed.Problems, seeds: true)).Plan!;
    plan.SeedsToCreate.Should().Be(2);
    var racer = await kit.AddTemplateAsync("DAILY");

    var outcome = await kit.Applier.ApplyAsync(plan, GamepackPreviousHandling.Keep);

    outcome.Status.Should().Be(GamepackApplyStatus.Applied);
    outcome.SeedTemplatesCreated.Should().Equal("Weekly");
    outcome.SeedTemplatesSkipped.Should().ContainSingle().Which.Should().Be(new SeedSkip("Daily", SeedSkipReasons.Exists));
    var list = await kit.Templates.ListAsync();
    list.Select(t => t.Name).Should().BeEquivalentTo("DAILY", "Weekly");
    list.Single(t => t.Name == "DAILY").Id.Should().Be(racer.Id);
    list.Single(t => t.Name == "DAILY").Entries.Should().BeEmpty();
  }
}
