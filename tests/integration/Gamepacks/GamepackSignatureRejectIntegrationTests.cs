using System;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using GameBot.Domain.Gamepacks;
using GameBot.Domain.Gamepacks.Signing;
using GameBot.UnitTests.Gamepacks;
using GameBot.UnitTests.Gamepacks.Signing;
using Xunit;

#pragma warning disable CA2007, CA1861, CA1707, CA1416, CA2000

namespace GameBot.IntegrationTests.Gamepacks;

/// <summary>Feature 146 (T021, T031): real stores. A rejected install changes nothing. Settings survive a restart.</summary>
public sealed class GamepackSignatureRejectIntegrationTests : IDisposable {
  private readonly GamepackTestKit _kit = new();
  private readonly TestSigner _signer = new();

  public void Dispose() {
    _signer.Dispose();
    _kit.Dispose();
  }

  private GamepackService NewService() {
    var checker = new GamepackSignatureChecker(new GamepackSignatureSettingsStore(_kit.Root), new GamepackSignatureVerifier());
    return new GamepackService(_kit.Sequences, _kit.Commands, _kit.Images, _kit.Store, _kit.Validator, _kit.Applier, signatureChecker: checker);
  }

  private static string Pack(string sequenceName = "Help All") => GamepackTestKit.Pack("pns-daily", "1.0.0",
    sequences: new[] { GamepackTestKit.Seq("a1", sequenceName, "c1") },
    commands: new[] { GamepackTestKit.Cmd("c1", "Tap help", imageId: "help-icon") },
    images: new[] { GamepackTestKit.Img("help-icon") });

  [Fact]
  public async Task EachRejectedInstallLeavesAllStateEqual() {
    var settings = new GamepackSignatureSettingsStore(_kit.Root);
    settings.SetPolicy(SignaturePolicy.Required);
    settings.AddKey("Builder", _signer.PublicKey);
    using var stranger = new TestSigner();
    using var service = NewService();
    var pack = Pack();
    var cases = new (string Pack, string? Signature)[] {
      (pack, null),
      (pack, _signer.SignWithBadSignature(pack)),
      (Pack("Changed"), _signer.Sign(pack)),
      (pack, stranger.Sign(pack))
    };

    foreach (var (text, signature) in cases) {
      var before = _kit.Snapshot();
      var outcome = await service.InstallAsync(text, dryRun: false, signature, default);
      outcome.Status.Should().Be(GamepackInstallStatus.SignatureRejected);
      outcome.Problems.Single().Code.Should().StartWith("signature_");
      _kit.Snapshot().Should().Equal(before);
      (await _kit.Sequences.ListAsync()).Should().BeEmpty();
      (await _kit.Commands.ListAsync()).Should().BeEmpty();
      (await _kit.Images.ListIdsAsync()).Should().BeEmpty();
      _kit.Store.Load().Gamepacks.Should().BeEmpty();
    }
  }

  [Fact]
  public async Task AValidSignatureInstalls() {
    var settings = new GamepackSignatureSettingsStore(_kit.Root);
    settings.SetPolicy(SignaturePolicy.Required);
    settings.AddKey("Builder", _signer.PublicKey);
    using var service = NewService();

    var outcome = await service.InstallAsync(Pack(), dryRun: false, _signer.Sign(Pack()), default);

    outcome.Status.Should().Be(GamepackInstallStatus.Ok);
    outcome.Signature!.Status.Should().Be(SignatureStatus.Valid);
    (await _kit.Sequences.ListAsync()).Should().HaveCount(1);
  }

  [Fact]
  public async Task ANewStoreAndServiceOnTheSameRootKeepThePolicyAndKeys() {
    var first = new GamepackSignatureSettingsStore(_kit.Root);
    first.SetPolicy(SignaturePolicy.Required);
    first.AddKey("Builder", _signer.PublicKey);

    using var restarted = NewService();
    var outcome = await restarted.InstallAsync(Pack(), dryRun: true, _signer.Sign(Pack()), default);

    outcome.Status.Should().Be(GamepackInstallStatus.Ok);
    outcome.Signature!.Should().Be(new SignatureCheckResult(SignatureStatus.Valid, SignaturePolicy.Required, WouldReject: false));
  }
}
