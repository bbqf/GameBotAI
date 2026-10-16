using System.Net;
using System.Threading.Tasks;
using FluentAssertions;
using GameBot.UnitTests.Gamepacks.Signing;
using Xunit;

#pragma warning disable CA2007, CA1861, CA1707, CA2000

namespace GameBot.ContractTests.Gamepacks;

/// <summary>Feature 146 (T022): the install route under the policy optional. Every gamepack installs.</summary>
public sealed class GamepackSignatureOptionalContractTests {
  [Theory]
  [InlineData("valid")]
  [InlineData("missing")]
  [InlineData("invalid")]
  [InlineData("content_mismatch")]
  [InlineData("untrusted")]
  public async Task EveryCaseInstallsAndShowsItsStatus(string status) {
    using var host = new GamepackContractHost();
    using var signer = new TestSigner();
    using var stranger = new TestSigner();
    await SigningContractKit.ConfigureAsync(host, "optional", signer);
    var pack = SigningContractKit.Pack();
    string? signature = signer.Sign(pack);
    if (status != "valid") {
      var bad = SigningContractKit.BadCase(status, signer, stranger);
      pack = bad.Pack;
      signature = bad.Signature;
    }

    var response = await host.InstallSignedAsync(pack, signature);

    response.StatusCode.Should().Be(HttpStatusCode.OK);
    (await SigningContractKit.SignatureBlockAsync(response)).ShouldBeStatus(status, "optional", wouldReject: false);
    (await host.GetJsonAsync("/api/gamepacks")).GetArrayLength().Should().Be(1);
  }

  [Fact]
  public async Task ARawJsonRequestStillInstallsAndShowsMissing() {
    using var host = new GamepackContractHost();
    await host.SetPolicyAsync("optional");

    var response = await host.InstallAsync(SigningContractKit.Pack());

    response.StatusCode.Should().Be(HttpStatusCode.OK);
    (await SigningContractKit.SignatureBlockAsync(response)).ShouldBeStatus("missing", "optional", wouldReject: false);
  }
}
