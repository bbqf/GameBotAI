using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using GameBot.Domain.Commands;
using GameBot.Domain.Gamepacks;
using GameBot.UnitTests.Gamepacks;
using Xunit;
using M = GameBot.UnitTests.Gamepacks.GamepackMemberOrderFixtures;

#pragma warning disable CA2007, CA1861, CA1707, CA1416, CA1305, CA1307, CA1310

namespace GameBot.IntegrationTests.Gamepacks;

/// <summary>Feature 154: a real install stores the same objects for every member order, and a bad type stores nothing.</summary>
public sealed class GamepackMemberOrderIntegrationTests {
  private static async Task<CommandSequence> InstallAndReadAsync(string condition) {
    using var kit = new GamepackTestKit();
    var outcome = await kit.Service.InstallAsync(M.PackWithCondition(condition), dryRun: false);
    outcome.Status.Should().Be(GamepackInstallStatus.Ok, string.Join("; ", outcome.Problems.Select(p => p.Message)));
    return (await kit.SequenceByNameAsync(GamepackStepFixtures.SequenceName))!;
  }

  [Fact]
  public async Task TheTypeMemberLastStoresTheSameConditionAsTheTypeMemberFirst() {
    var first = await InstallAndReadAsync(M.ConditionFirst);
    var last = await InstallAndReadAsync(M.ConditionLast);

    var a = first.Steps.Single(s => s.StepId == "c1").Condition;
    var b = last.Steps.Single(s => s.StepId == "c1").Condition;
    b.Should().BeOfType<CommandOutcomeStepCondition>();
    b.Should().BeEquivalentTo(a, o => o.RespectingRuntimeTypes());
  }

  [Fact]
  public async Task AMissingTypeAnswersInvalidJsonAndStoresNothing() {
    using var kit = new GamepackTestKit();

    var outcome = await kit.Service.InstallAsync(M.PackWithCondition(M.MissingType()), dryRun: false);

    outcome.Status.Should().Be(GamepackInstallStatus.InvalidJson);
    (await kit.Sequences.ListAsync()).Should().BeEmpty();
    (await kit.Commands.ListAsync()).Should().BeEmpty();
  }
}
