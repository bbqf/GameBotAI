using System;
using System.Net;
using System.Net.Http.Json;
using System.Threading.Tasks;
using FluentAssertions;
using GameBot.UnitTests.Gamepacks.Signing;
using Xunit;

#pragma warning disable CA2007, CA1861, CA1707, CA2000

namespace GameBot.ContractTests.Gamepacks;

/// <summary>Feature 146 (T029, SC-005, SC-006): policy and key changes apply to the next install with no restart.</summary>
public sealed class GamepackSignatureTrustContractTests {
  [Fact]
  public async Task AddKeyThenInstall_UsesNewKeyWithoutRestart() {
    using var host = new GamepackContractHost();
    using var signer = new TestSigner();
    await host.SetPolicyAsync("required");
    var pack = SigningContractKit.Pack();
    var signature = signer.Sign(pack);

    var first = await host.InstallSignedAsync(pack, signature);
    first.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    (await SigningContractKit.SignatureBlockAsync(first)).GetProperty("status").GetString().Should().Be("untrusted");

    await host.AddKeyAsync("Builder", signer.PublicKey);
    var second = await host.InstallSignedAsync(pack, signature);

    second.StatusCode.Should().Be(HttpStatusCode.OK);
    (await SigningContractKit.SignatureBlockAsync(second)).GetProperty("status").GetString().Should().Be("valid");
  }

  [Fact]
  public async Task ChangePolicyThenInstall_UsesNewPolicyWithoutRestart() {
    using var host = new GamepackContractHost();
    await host.SetPolicyAsync("required");
    var pack = SigningContractKit.Pack();
    (await host.InstallSignedAsync(pack, signature: null)).StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);

    await host.SetPolicyAsync("optional");
    var optional = await host.InstallSignedAsync(pack, signature: null);
    optional.StatusCode.Should().Be(HttpStatusCode.OK);
    (await SigningContractKit.SignatureBlockAsync(optional)).ShouldBeStatus("missing", "optional", wouldReject: false);
  }

  [Fact]
  public async Task RemoveKeyThenInstall_RejectsAsUntrusted() {
    using var host = new GamepackContractHost();
    using var signer = new TestSigner();
    await SigningContractKit.ConfigureAsync(host, "required", signer);
    var pack = SigningContractKit.Pack();
    var signature = signer.Sign(pack);
    (await host.InstallSignedAsync(pack, signature)).StatusCode.Should().Be(HttpStatusCode.OK);

    (await host.Client.DeleteAsync(new Uri("/api/gamepacks/signature/keys/" + signer.Fingerprint, UriKind.Relative)))
      .StatusCode.Should().Be(HttpStatusCode.NoContent);
    var again = await host.InstallSignedAsync(SigningContractKit.Pack(version: "1.0.1"), signer.Sign(SigningContractKit.Pack(version: "1.0.1")));

    again.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    (await SigningContractKit.SignatureBlockAsync(again)).GetProperty("status").GetString().Should().Be("untrusted");
  }

  [Fact]
  public async Task NoKeyUnderRequiredRejectsASignedGamepack() {
    using var host = new GamepackContractHost();
    using var signer = new TestSigner();
    await host.SetPolicyAsync("required");
    var pack = SigningContractKit.Pack();

    var response = await host.InstallSignedAsync(pack, signer.Sign(pack));

    response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
  }

  [Fact]
  public async Task TwoTrustedKeysEitherGivesValid() {
    using var host = new GamepackContractHost();
    using var one = new TestSigner();
    using var two = new TestSigner();
    await host.SetPolicyAsync("required");
    await host.AddKeyAsync("One", one.PublicKey);
    await host.AddKeyAsync("Two", two.PublicKey);
    var pack = SigningContractKit.Pack();

    (await host.InstallSignedAsync(pack, one.Sign(pack), dryRun: true)).StatusCode.Should().Be(HttpStatusCode.OK);
    var first = await SigningContractKit.SignatureBlockAsync(await host.InstallSignedAsync(pack, one.Sign(pack), dryRun: true));
    var second = await SigningContractKit.SignatureBlockAsync(await host.InstallSignedAsync(pack, two.Sign(pack), dryRun: true));
    first.ShouldBeStatus("valid", "required", wouldReject: false);
    second.ShouldBeStatus("valid", "required", wouldReject: false);
  }
}
