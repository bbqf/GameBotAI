using System.Linq;
using System.Net;
using System.Threading.Tasks;
using FluentAssertions;
using Xunit;
using M = GameBot.UnitTests.Gamepacks.GamepackMemberOrderFixtures;

#pragma warning disable CA2007, CA1861, CA1707, CA2000, CA1305, CA1307, CA1310

namespace GameBot.ContractTests.Gamepacks;

/// <summary>Feature 154: a missing or bad type member gives 400 with the JSON path, never 500.</summary>
public sealed class GamepackBadTypeContractTests {
  public static TheoryData<string, string, bool> Cases() {
    var data = new TheoryData<string, string, bool>();
    foreach (var dryRun in new[] { true, false }) {
      data.Add(M.MissingType(), "the type member is missing", dryRun);
      data.Add(M.UnknownType(), "the type value is unknown", dryRun);
      data.Add(M.NumberType(), "the type value must be a string", dryRun);
      data.Add(M.DuplicateType(), "the type member is duplicated", dryRun);
    }

    return data;
  }

  [Theory]
  [MemberData(nameof(Cases))]
  public async Task ABadTypeGives400WithThePath(string condition, string reason, bool dryRun) {
    using var host = new GamepackContractHost();

    var response = await host.InstallAsync(M.PackWithCondition(condition), dryRun);

    response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    var body = await GamepackContractHost.ReadAsync(response);
    body.GetProperty("valid").GetBoolean().Should().BeFalse();
    var message = body.GetProperty("problems").EnumerateArray().Single().GetProperty("message").GetString();
    message.Should().Contain("$.sequences[0].steps[1].condition").And.Contain(reason);
  }

  [Theory]
  [InlineData(true)]
  [InlineData(false)]
  public async Task ANestedBadTypeGivesTheNestedPath(bool dryRun) {
    using var host = new GamepackContractHost();

    var response = await host.InstallAsync(M.PackWithNestedBreak(M.NestedBadItem), dryRun);

    response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    var body = await GamepackContractHost.ReadAsync(response);
    body.GetProperty("problems").EnumerateArray().Single().GetProperty("message").GetString()
      .Should().Contain("$.sequences[0].steps[0].breakCondition.children[1]");
  }
}
