using System.Net;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using GameBot.UnitTests.Gamepacks.Signing;
using Xunit;

#pragma warning disable CA2007, CA1861, CA1707, CA2000

namespace GameBot.ContractTests.Gamepacks;

/// <summary>Feature 146 (T020): the install route under the policy required.</summary>
public sealed class GamepackSignatureRequiredContractTests {
  [Fact]
  public async Task AValidSignedGamepackInstallsAndReportsValid() {
    using var host = new GamepackContractHost();
    using var signer = new TestSigner();
    await SigningContractKit.ConfigureAsync(host, "required", signer);
    var pack = SigningContractKit.Pack();

    var response = await host.InstallSignedAsync(pack, signer.Sign(pack));

    response.StatusCode.Should().Be(HttpStatusCode.OK);
    (await SigningContractKit.SignatureBlockAsync(response)).ShouldBeStatus("valid", "required", wouldReject: false);
    (await host.GetJsonAsync("/api/gamepacks")).GetArrayLength().Should().Be(1);
  }

  [Theory]
  [InlineData("missing")]
  [InlineData("invalid")]
  [InlineData("content_mismatch")]
  [InlineData("untrusted")]
  public async Task EachBadCaseGives422WithItsCodeAndChangesNothing(string status) {
    using var host = new GamepackContractHost();
    using var signer = new TestSigner();
    using var stranger = new TestSigner();
    await SigningContractKit.ConfigureAsync(host, "required", signer);
    var (_, pack, signature) = SigningContractKit.BadCase(status, signer, stranger);
    var before = await host.StateSnapshotAsync();

    var response = await host.InstallSignedAsync(pack, signature);

    response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    var body = await GamepackContractHost.ReadAsync(response);
    body.GetProperty("valid").GetBoolean().Should().BeFalse();
    body.GetProperty("result").GetString().Should().Be("signature_rejected");
    var problem = body.GetProperty("problems")[0];
    problem.GetProperty("category").GetString().Should().Be("signature");
    problem.GetProperty("code").GetString().Should().Be("signature_" + status);
    body.GetProperty("signature").ShouldBeStatus(status, "required", wouldReject: true);
    (await host.StateSnapshotAsync()).Should().Be(before);
  }

  [Fact]
  public async Task TheRejectionTextHasNoHashKeyOrOffset() {
    using var host = new GamepackContractHost();
    using var signer = new TestSigner();
    using var stranger = new TestSigner();
    await SigningContractKit.ConfigureAsync(host, "required", signer);
    var pack = SigningContractKit.Pack();
    var signature = stranger.Sign(pack);

    var response = await host.InstallSignedAsync(pack, signature);

    var text = await response.Content.ReadAsStringAsync();
    text.Should().NotContain(stranger.PublicKey).And.NotContain(stranger.Fingerprint).And.NotContainEquivalentOf("offset");
    using var doc = JsonDocument.Parse(signature);
    text.Should().NotContain(doc.RootElement.GetProperty("manifestSha256").GetString()!);
  }

  [Fact]
  public async Task AnUnparsableGamepackGivesTheExistingParseError() {
    using var host = new GamepackContractHost();
    using var signer = new TestSigner();
    await SigningContractKit.ConfigureAsync(host, "required", signer);

    var response = await host.InstallSignedAsync("{ not json", signature: null);

    response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    GamepackContractHost.ProblemCategories(await GamepackContractHost.ReadAsync(response)).Should().Equal("structure");
  }

  [Fact]
  public async Task RollbackAndListRunNoSignatureCheck() {
    using var host = new GamepackContractHost();
    using var signer = new TestSigner();
    await SigningContractKit.ConfigureAsync(host, "required", signer);
    var v1 = SigningContractKit.Pack(version: "1.0.0");
    var v2 = SigningContractKit.Pack(version: "2.0.0");
    (await host.InstallSignedAsync(v1, signer.Sign(v1))).StatusCode.Should().Be(HttpStatusCode.OK);
    (await host.InstallSignedAsync(v2, signer.Sign(v2))).StatusCode.Should().Be(HttpStatusCode.OK);

    (await host.Client.GetAsync(new System.Uri("/api/gamepacks", System.UriKind.Relative))).StatusCode.Should().Be(HttpStatusCode.OK);
    var rollback = await host.RollbackAsync("pns-daily");

    rollback.StatusCode.Should().Be(HttpStatusCode.OK);
  }
}
