using System.Linq;
using System.Text.Json.Nodes;
using FluentAssertions;
using GameBot.Domain.Gamepacks;
using GameBot.Domain.QueueTemplates;
using Xunit;

#pragma warning disable CA1707, CA1861, CA1307

namespace GameBot.UnitTests.Gamepacks;

/// <summary>Feature 150 (T008): the file member seedTemplates.</summary>
public sealed class GamepackFileSerializerSeedTests {
  private static string WithSeed() => GamepackTestKit.Pack("p", "1",
    sequences: new[] { GamepackTestKit.Seq("a1", "Help All") },
    seedTemplates: new[] { GamepackTestKit.Seed("Daily", GamepackTestKit.SeedEntry("Help All", "Timer", timerTimeOfDay: "03:30:00")) });

  [Fact]
  public void AFileWithSeedsParsesThem() {
    var parsed = GamepackFileSerializer.Parse(WithSeed());

    parsed.Problems.Should().BeEmpty();
    var seed = parsed.File!.SeedTemplates.Single();
    seed.Name.Should().Be("Daily");
    seed.Entries.Single().Sequence.Should().Be("Help All");
    seed.Entries.Single().ScheduleType.Should().Be(ScheduleType.Timer);
    seed.Entries.Single().TimerTimeOfDay.Should().NotBeNull();
  }

  [Fact]
  public void TheMemberNameMatchesWithoutCase() {
    var text = WithSeed().Replace("seedTemplates", "SEEDTEMPLATES");

    GamepackFileSerializer.Parse(text).File!.SeedTemplates.Should().HaveCount(1);
  }

  [Fact]
  public void AFileWithoutTheMemberHasNoSeeds() {
    var parsed = GamepackFileSerializer.Parse(GamepackTestKit.Pack("p", "1"));

    parsed.File!.SeedTemplates.Should().BeEmpty();
    parsed.Problems.Should().BeEmpty();
  }

  [Fact]
  public void AWrongShapeGivesAStructureProblem() {
    var text = GamepackTestKit.Pack("p", "1").Replace("\"images\":[]", "\"images\":[],\"seedTemplates\":\"x\"");

    var parsed = GamepackFileSerializer.Parse(text);

    parsed.Problems.Should().ContainSingle(p => p.Category == GamepackProblemCategories.Structure && p.ObjectType == GamepackObjectTypes.SeedTemplate);
  }

  [Fact]
  public void SerializeWritesTheSeedsAndTheyRoundTrip() {
    var file = GamepackFileSerializer.Parse(WithSeed()).File!;

    var text = GamepackFileSerializer.Serialize(file);

    JsonNode.Parse(text)!.AsObject().ContainsKey("seedTemplates").Should().BeTrue();
    GamepackFileSerializer.Parse(text).File!.SeedTemplates.Single().Name.Should().Be("Daily");
  }

  [Fact]
  public void AFileWithoutSeedsIsWrittenWithoutTheMemberAndStaysTheSame() {
    var file = GamepackFileSerializer.Parse(GamepackTestKit.Pack("p", "1", sequences: new[] { GamepackTestKit.Seq("a1", "Help All") })).File!;

    var first = GamepackFileSerializer.Serialize(file);
    var second = GamepackFileSerializer.Serialize(GamepackFileSerializer.Parse(first).File!);

    first.Should().NotContain("seedTemplates");
    second.Should().Be(first);
    JsonNode.Parse(first)!.AsObject().Select(p => p.Key).Should().Equal("manifest", "sequences", "commands", "images");
  }
}
