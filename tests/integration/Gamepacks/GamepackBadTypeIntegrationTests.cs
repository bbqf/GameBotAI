using System.Threading.Tasks;
using FluentAssertions;
using GameBot.Domain.Gamepacks;
using GameBot.UnitTests.Gamepacks;
using Xunit;
using M = GameBot.UnitTests.Gamepacks.GamepackMemberOrderFixtures;

#pragma warning disable CA2007, CA1707, CA1416

namespace GameBot.IntegrationTests.Gamepacks;

/// <summary>Feature 154: a dry run and a real install of a pack with a bad type both answer InvalidJson.</summary>
public sealed class GamepackBadTypeIntegrationTests {
  [Theory]
  [InlineData(true)]
  [InlineData(false)]
  public async Task AnUnknownTypeAnswersInvalidJson(bool dryRun) {
    using var kit = new GamepackTestKit();

    var outcome = await kit.Service.InstallAsync(M.PackWithCondition(M.UnknownType()), dryRun);

    outcome.Status.Should().Be(GamepackInstallStatus.InvalidJson);
  }
}
