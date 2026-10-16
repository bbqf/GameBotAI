using System.Net;
using System.Threading.Tasks;
using FluentAssertions;
using GameBot.UnitTests.Gamepacks.Signing;
using Xunit;

#pragma warning disable CA2007, CA1861, CA1707, CA2000

namespace GameBot.ContractTests.Gamepacks;

/// <summary>Feature 146 (T024): the dry run shows wouldReject and changes no state (FR-014).</summary>
public sealed class GamepackSignatureDryRunContractTests {
  [Theory]
  [InlineData("required", true, true)]
  [InlineData("required", false, false)]
  [InlineData("optional", true, false)]
  [InlineData("optional", false, false)]
  [InlineData("off", true, false)]
  [InlineData("off", false, false)]
  public async Task WouldRejectFollowsThePolicyAndTheStatus(string policy, bool badSignature, bool expected) {
    using var host = new GamepackContractHost();
    using var signer = new TestSigner();
    await SigningContractKit.ConfigureAsync(host, policy, signer);
    var pack = SigningContractKit.Pack();
    var signature = badSignature ? signer.SignWithBadSignature(pack) : signer.Sign(pack);
    var before = await host.StateSnapshotAsync();

    var response = await host.InstallSignedAsync(pack, signature, dryRun: true);

    response.StatusCode.Should().Be(HttpStatusCode.OK);
    var block = await SigningContractKit.SignatureBlockAsync(response);
    block.GetProperty("policy").GetString().Should().Be(policy);
    block.GetProperty("wouldReject").GetBoolean().Should().Be(expected);
    (await host.StateSnapshotAsync()).Should().Be(before);
  }

  [Fact]
  public async Task ABadSignatureUnderRequiredGivesWouldRejectTrueWithStatus() {
    using var host = new GamepackContractHost();
    using var signer = new TestSigner();
    await SigningContractKit.ConfigureAsync(host, "required", signer);

    var response = await host.InstallSignedAsync(SigningContractKit.Pack(), signature: null, dryRun: true);

    response.StatusCode.Should().Be(HttpStatusCode.OK);
    (await SigningContractKit.SignatureBlockAsync(response)).ShouldBeStatus("missing", "required", wouldReject: true);
  }
}
