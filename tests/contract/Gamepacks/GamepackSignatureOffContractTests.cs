using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using FluentAssertions;
using GameBot.Domain.Gamepacks.Signing;
using GameBot.UnitTests.Gamepacks.Signing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

#pragma warning disable CA2007, CA1861, CA1707, CA2000

namespace GameBot.ContractTests.Gamepacks;

/// <summary>Feature 146 (T030, FR-009, SC-004, SC-001): under the policy off, no signature is checked.</summary>
public sealed class GamepackSignatureOffContractTests {
  private sealed class SpyVerifier : IGamepackSignatureVerifier {
    public int Calls;

    public SignatureStatus Verify(string gamepackJson, string? signatureText, IReadOnlyCollection<TrustedKey> trustedKeys) {
      Calls++;
      return new GamepackSignatureVerifier().Verify(gamepackJson, signatureText, trustedKeys);
    }
  }

  private static GamepackContractHost HostWith(SpyVerifier spy) => new(configure: (services, _) => {
    services.RemoveAll<IGamepackSignatureVerifier>();
    services.AddSingleton<IGamepackSignatureVerifier>(spy);
  });

  [Fact]
  public async Task UnderOffTheVerifierIsNeverCalledAndTheStatusIsNotChecked() {
    var spy = new SpyVerifier();
    using var host = HostWith(spy);
    using var signer = new TestSigner();
    var pack = SigningContractKit.Pack();

    // The signature part is not JSON. Under off, the route ignores it.
    var response = await host.InstallSignedAsync(pack, signature: null, signaturePart: new StringContent("garbage", Encoding.UTF8, "application/json"));

    response.StatusCode.Should().Be(HttpStatusCode.OK);
    (await SigningContractKit.SignatureBlockAsync(response)).ShouldBeStatus("not_checked", "off", wouldReject: false);
    spy.Calls.Should().Be(0);
    (await host.GetJsonAsync("/api/gamepacks")).GetArrayLength().Should().Be(1);
  }

  [Fact]
  public async Task ARawJsonInstallUnderOffFollowsTheExistingBehavior() {
    var spy = new SpyVerifier();
    using var host = HostWith(spy);

    var response = await host.InstallAsync(SigningContractKit.Pack());

    response.StatusCode.Should().Be(HttpStatusCode.OK);
    (await SigningContractKit.SignatureBlockAsync(response)).ShouldBeStatus("not_checked", "off", wouldReject: false);
    spy.Calls.Should().Be(0);
  }

  [Theory]
  [InlineData("required")]
  [InlineData("optional")]
  [InlineData("off")]
  public async Task AValidGamepackInstallsUnderAllThreePolicies(string policy) {
    using var host = new GamepackContractHost();
    using var signer = new TestSigner();
    await SigningContractKit.ConfigureAsync(host, policy, signer);
    var pack = SigningContractKit.Pack();

    var response = await host.InstallSignedAsync(pack, signer.Sign(pack));

    response.StatusCode.Should().Be(HttpStatusCode.OK);
    (await GamepackContractHost.ReadAsync(response)).GetProperty("valid").GetBoolean().Should().BeTrue();
  }
}
