using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using GameBot.Domain.Gamepacks;
using Xunit;

#pragma warning disable CA1707, CA2007, CA1861, CA1416

namespace GameBot.UnitTests.Gamepacks;

/// <summary>Feature 149 (T013): the validator decision for a take-over.</summary>
public sealed class GamepackValidatorTakeOverTests {
  private static string UserPack() => GamepackTestKit.Pack("user-objects", "1",
    sequences: new[] { GamepackTestKit.Seq("u-seq", "Help All", "u-cmd") },
    commands: new[] { GamepackTestKit.Cmd("u-cmd", "Tap help") });

  private static string Pack(string name = "pns", string version = "1") => GamepackTestKit.Pack(name, version,
    sequences: new[] { GamepackTestKit.Seq("p-seq", "Help All", "p-cmd") },
    commands: new[] { GamepackTestKit.Cmd("p-cmd", "Tap help") });

  private static async Task<GamepackValidationResult> CheckAsync(GamepackTestKit kit, string json, bool takeOver) {
    var parsed = GamepackFileSerializer.Parse(json);
    return await kit.Validator.ValidateAsync(parsed.File!, parsed.Problems, takeOver: takeOver);
  }

  [Fact]
  public async Task AUserOwnedObjectIsATakeOverWhenTheOptionIsOn() {
    using var kit = new GamepackTestKit();
    await kit.AddUserObjectsAsync(UserPack());
    var userSequence = (await kit.SequenceByNameAsync("Help All"))!;

    var result = await CheckAsync(kit, Pack(), takeOver: true);

    result.IsValid.Should().BeTrue();
    result.Plan!.TakenOver.Should().Be(2);
    result.Plan.Updated.Should().Be(0);
    result.Plan.Created.Should().Be(0);
    var write = result.Plan.Writes.Single(w => w.Name == "Help All");
    write.IsTakeOver.Should().BeTrue();
    write.IsUpdate.Should().BeTrue();
    write.FinalId.Should().Be(userSequence.Id);
    result.Plan.TakeOvers.Select(t => t.Type + ":" + t.Name).Should().BeEquivalentTo("sequence:Help All", "command:Tap help");
  }

  [Fact]
  public async Task TheOptionOffKeepsTheConflict() {
    using var kit = new GamepackTestKit();
    await kit.AddUserObjectsAsync(UserPack());

    var result = await CheckAsync(kit, Pack(), takeOver: false);

    result.IsValid.Should().BeFalse();
    result.Problems.Should().OnlyContain(p => p.Category == GamepackProblemCategories.Conflict && p.Owner == "user");
  }

  [Theory]
  [InlineData(false)]
  [InlineData(true)]
  public async Task AnObjectOfAnotherGamepackStaysAConflict(bool takeOver) {
    using var kit = new GamepackTestKit();
    (await kit.Service.InstallAsync(Pack("other"), dryRun: false)).Status.Should().Be(GamepackInstallStatus.Ok);

    var result = await CheckAsync(kit, Pack("pns"), takeOver);

    result.IsValid.Should().BeFalse();
    result.Problems.Should().OnlyContain(p => p.Category == GamepackProblemCategories.Conflict && p.Owner!.StartsWith("other", System.StringComparison.Ordinal));
  }

  [Fact]
  public async Task AnObjectOfTheSameGamepackIsAnUpdateAndNotATakeOver() {
    using var kit = new GamepackTestKit();
    (await kit.Service.InstallAsync(Pack("pns", "1"), dryRun: false)).Status.Should().Be(GamepackInstallStatus.Ok);

    var result = await CheckAsync(kit, Pack("pns", "2"), takeOver: true);

    result.IsValid.Should().BeTrue();
    result.Plan!.Updated.Should().Be(2);
    result.Plan.TakenOver.Should().Be(0);
    result.Plan.TakeOvers.Should().BeEmpty();
  }

  [Fact]
  public async Task OnlyTheFirstOfTwoUserObjectsWithTheSameNameIsTakenOver() {
    using var kit = new GamepackTestKit();
    await kit.AddUserObjectsAsync(GamepackTestKit.Pack("u", "1", sequences: new[] { GamepackTestKit.Seq("a", "Help All"), GamepackTestKit.Seq("b", "help all") }));

    var result = await CheckAsync(kit, Pack(), takeOver: true);

    result.IsValid.Should().BeFalse();
    result.Problems.Should().ContainSingle().Which.Category.Should().Be(GamepackProblemCategories.Conflict);
  }

  [Fact]
  public async Task ObjectsThatAreNotInThePackDoNotChange() {
    using var kit = new GamepackTestKit();
    await kit.AddUserObjectsAsync(GamepackTestKit.Pack("u", "1", sequences: new[] { GamepackTestKit.Seq("other", "Other") }));

    var result = await CheckAsync(kit, Pack(), takeOver: true);

    result.IsValid.Should().BeTrue();
    result.Plan!.TakenOver.Should().Be(0);
    result.Plan.Created.Should().Be(2);
  }

  [Fact]
  public async Task AnObjectWithTheSameNameAndAnotherTypeIsNotMatched() {
    using var kit = new GamepackTestKit();
    await kit.AddUserObjectsAsync(GamepackTestKit.Pack("u", "1", commands: new[] { GamepackTestKit.Cmd("c", "Help All") }));

    var result = await CheckAsync(kit, Pack(), takeOver: true);

    result.IsValid.Should().BeTrue();
    result.Plan!.TakenOver.Should().Be(0);
    result.Plan.Writes.Single(w => w.Name == "Help All").IsTakeOver.Should().BeFalse();
  }

  [Fact]
  public async Task TheIdenticalMarkFollowsTheContent() {
    using var kit = new GamepackTestKit();
    await kit.AddUserObjectsAsync(GamepackTestKit.Pack("u", "1",
      sequences: new[] { GamepackTestKit.Seq("u-seq", "Help All", "u-cmd") },
      commands: new[] { GamepackTestKit.Cmd("u-cmd", "Tap help", imageId: "different") }));

    var result = await CheckAsync(kit, Pack(), takeOver: true);

    result.IsValid.Should().BeTrue();
    result.Plan!.TakeOvers.Single(t => t.Type == "command").Identical.Should().BeFalse();
    result.Plan.TakeOvers.Single(t => t.Type == "sequence").Identical.Should().BeTrue();
  }
}
