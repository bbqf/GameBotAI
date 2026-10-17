using System.Linq;
using System.Net;
using System.Threading.Tasks;
using FluentAssertions;
using Xunit;
using M = GameBot.UnitTests.Gamepacks.GamepackMemberOrderFixtures;

#pragma warning disable CA2007, CA1861, CA1707, CA2000, CA1305, CA1307, CA1310

namespace GameBot.ContractTests.Gamepacks;

/// <summary>Feature 154: the type member of a condition can be at any position, for dry run and real install.</summary>
public sealed class GamepackMemberOrderContractTests {
  [Theory]
  [InlineData(M.ConditionFirst, true)]
  [InlineData(M.ConditionMiddle, true)]
  [InlineData(M.ConditionLast, true)]
  [InlineData(M.ConditionFirst, false)]
  [InlineData(M.ConditionMiddle, false)]
  [InlineData(M.ConditionLast, false)]
  public async Task AnyMemberOrderIsAccepted(string condition, bool dryRun) {
    using var host = new GamepackContractHost();

    var response = await host.InstallAsync(M.PackWithCondition(condition), dryRun);

    response.StatusCode.Should().Be(HttpStatusCode.OK);
    var body = await GamepackContractHost.ReadAsync(response);
    body.GetProperty("valid").GetBoolean().Should().BeTrue();
  }
}
