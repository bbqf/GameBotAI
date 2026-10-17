using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using GameBot.Domain.Gamepacks;
using GameBot.UnitTests.Gamepacks;
using Xunit;
using M = GameBot.UnitTests.Gamepacks.GamepackMemberOrderFixtures;

#pragma warning disable CA2007, CA1861, CA1707, CA1416, CA1305, CA1307, CA1310

namespace GameBot.IntegrationTests.Gamepacks;

/// <summary>Feature 154: a large pack with late type members is accepted. The test makes no assertion on time.</summary>
public sealed class GamepackLargePackSmokeTests {
  private static string PackOf(int count) {
    var sequences = new List<object>();
    for (var i = 0; i < count; i++) {
      var conditional = $"{{\"stepId\":\"c1\",\"stepType\":\"Conditional\",\"condition\":{M.ConditionLast},\"body\":[{GamepackStepFixtures.PrimitiveOnly("s2")}]}}";
      var steps = new[] { GamepackStepFixtures.PrimitiveOnly("s0"), conditional };
      sequences.Add(JsonSerializer.Deserialize<JsonElement>($"{{\"name\":\"Seq {i}\",\"steps\":[{string.Join(",", steps)}]}}"));
    }

    return GamepackTestKit.Pack(GamepackStepFixtures.PackName, "1.0.0", sequences: sequences.ToArray(),
      commands: new[] { GamepackTestKit.Cmd(GamepackStepFixtures.CommandId, GamepackStepFixtures.CommandName) });
  }

  [Theory]
  [InlineData(1)]
  [InlineData(300)]
  public async Task ADryRunOfAPackWithLateTypeMembersIsValid(int count) {
    using var kit = new GamepackTestKit();

    var outcome = await kit.Service.InstallAsync(PackOf(count), dryRun: true);

    outcome.Status.Should().Be(GamepackInstallStatus.Ok, string.Join("; ", outcome.Problems.Take(3).Select(p => p.Message)));
  }
}
