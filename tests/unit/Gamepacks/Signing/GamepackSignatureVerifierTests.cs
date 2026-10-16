using System;
using System.Linq;
using System.Text.Json;
using FluentAssertions;
using GameBot.Domain.Gamepacks.Signing;
using Xunit;

#pragma warning disable CA2007, CA1861, CA1707, CA2000, CA1305, CA1307, CA1310, CA1869

namespace GameBot.UnitTests.Gamepacks.Signing;

/// <summary>Feature 146 (T013): the hasher and the verifier.</summary>
public sealed class GamepackSignatureVerifierTests : IDisposable {
  private readonly TestSigner _signer = new();
  private readonly GamepackSignatureVerifier _verifier = new();

  public void Dispose() => _signer.Dispose();

  private static string Pack(string version = "1.0.0", string seqName = "Help All") => GamepackTestKit.Pack("pns-daily", version,
    sequences: new[] { GamepackTestKit.Seq("a1", seqName, "c1") },
    commands: new[] { GamepackTestKit.Cmd("c1", "Tap help", imageId: "help-icon") },
    images: new[] { GamepackTestKit.Img("help-icon") });

  private SignatureStatus Verify(string pack, string? signature, params TrustedKey[] keys) => _verifier.Verify(pack, signature, keys);

  [Fact]
  public void AValidSignatureFromATrustedKeyIsValid() {
    var pack = Pack();
    Verify(pack, _signer.Sign(pack), _signer.AsTrusted()).Should().Be(SignatureStatus.Valid);
  }

  [Fact]
  public void SameInputTwiceGivesTheSameStatus() {
    var pack = Pack();
    var signature = _signer.Sign(pack);
    Verify(pack, signature, _signer.AsTrusted()).Should().Be(Verify(pack, signature, _signer.AsTrusted()));
  }

  [Fact]
  public void LineBreaksAndKeyOrderDoNotChangeTheHashes() {
    var pack = Pack();
    var signature = _signer.Sign(pack);
    using var doc = JsonDocument.Parse(pack);
    var pretty = JsonSerializer.Serialize(doc.RootElement, new JsonSerializerOptions { WriteIndented = true });
    Verify(pretty, signature, _signer.AsTrusted()).Should().Be(SignatureStatus.Valid);
  }

  [Fact]
  public void NoSignatureIsMissing() {
    Verify(Pack(), null, _signer.AsTrusted()).Should().Be(SignatureStatus.Missing);
  }

  [Theory]
  [InlineData("")]
  [InlineData("   ")]
  [InlineData("not json")]
  [InlineData("{\"format\":\"other-1\"}")]
  [InlineData("[]")]
  public void AnEmptyOrUnknownSignatureIsInvalid(string text) {
    Verify(Pack(), text, _signer.AsTrusted()).Should().Be(SignatureStatus.Invalid);
  }

  [Fact]
  public void AChangedSignatureByteIsInvalid() {
    var pack = Pack();
    Verify(pack, _signer.SignWithBadSignature(pack), _signer.AsTrusted()).Should().Be(SignatureStatus.Invalid);
  }

  [Fact]
  public void ASignedListThatWasChangedAfterSigningIsInvalid() {
    var pack = Pack();
    var signature = _signer.Sign(pack);
    using var doc = JsonDocument.Parse(signature);
    var manifestHash = doc.RootElement.GetProperty("manifestSha256").GetString()!;
    Verify(pack, signature.Replace(manifestHash, new string('0', 64), StringComparison.Ordinal), _signer.AsTrusted())
      .Should().Be(SignatureStatus.Invalid);
  }

  [Fact]
  public void AChangedByteInAFileIsAContentMismatch() {
    var signature = _signer.Sign(Pack());
    Verify(Pack(seqName: "Help All 2"), signature, _signer.AsTrusted()).Should().Be(SignatureStatus.ContentMismatch);
  }

  [Fact]
  public void AChangedManifestIsAContentMismatch() {
    var signature = _signer.Sign(Pack("1.0.0"));
    Verify(Pack("1.0.1"), signature, _signer.AsTrusted()).Should().Be(SignatureStatus.ContentMismatch);
  }

  [Fact]
  public void AnExtraFileIsAContentMismatch() {
    var signature = _signer.Sign(Pack());
    var extra = GamepackTestKit.Pack("pns-daily", "1.0.0",
      sequences: new[] { GamepackTestKit.Seq("a1", "Help All", "c1") },
      commands: new[] { GamepackTestKit.Cmd("c1", "Tap help", imageId: "help-icon"), GamepackTestKit.Cmd("c2", "Extra") },
      images: new[] { GamepackTestKit.Img("help-icon") });
    Verify(extra, signature, _signer.AsTrusted()).Should().Be(SignatureStatus.ContentMismatch);
  }

  [Fact]
  public void AMissingFileIsAContentMismatch() {
    var signature = _signer.Sign(Pack());
    var fewer = GamepackTestKit.Pack("pns-daily", "1.0.0",
      sequences: new[] { GamepackTestKit.Seq("a1", "Help All", "c1") },
      commands: new[] { GamepackTestKit.Cmd("c1", "Tap help", imageId: "help-icon") });
    Verify(fewer, signature, _signer.AsTrusted()).Should().Be(SignatureStatus.ContentMismatch);
  }

  [Fact]
  public void AnUnknownFieldIsPartOfTheHash() {
    var pack = Pack();
    var signature = _signer.Sign(pack);
    var forged = pack.Replace("\"manifest\":{", "\"manifest\":{\"extra\":1,", StringComparison.Ordinal);
    Verify(forged, signature, _signer.AsTrusted()).Should().Be(SignatureStatus.ContentMismatch);
  }

  [Fact]
  public void ASoundSignatureFromAnUnknownKeyIsUntrusted() {
    var pack = Pack();
    using var other = new TestSigner();
    Verify(pack, _signer.Sign(pack), other.AsTrusted()).Should().Be(SignatureStatus.Untrusted);
    Verify(pack, _signer.Sign(pack)).Should().Be(SignatureStatus.Untrusted);
  }

  [Fact]
  public void ManyFaultsGiveTheFirstStatusInOrder() {
    // Bad crypto, changed content, and no trusted key at once: invalid comes first.
    var pack = Pack();
    var bad = _signer.SignWithBadSignature(pack);
    Verify(Pack(seqName: "Other"), bad).Should().Be(SignatureStatus.Invalid);
    // Changed content and no trusted key: content_mismatch comes before untrusted.
    Verify(Pack(seqName: "Other"), _signer.Sign(pack)).Should().Be(SignatureStatus.ContentMismatch);
    // No signature: missing comes before all.
    Verify(Pack(seqName: "Other"), null).Should().Be(SignatureStatus.Missing);
  }

  [Fact]
  public void TwoUnrelatedTrustedKeysBothGiveValidAndALabelChangeDoesNotMatter() {
    var pack = Pack();
    using var second = new TestSigner();
    var keys = new[] { _signer.AsTrusted("One"), second.AsTrusted("Two") };
    Verify(pack, _signer.Sign(pack), keys).Should().Be(SignatureStatus.Valid);
    Verify(pack, second.Sign(pack), keys).Should().Be(SignatureStatus.Valid);

    var relabeled = new[] { _signer.AsTrusted("Renamed"), second.AsTrusted("Other name") };
    Verify(pack, _signer.Sign(pack), relabeled).Should().Be(SignatureStatus.Valid);
  }

  [Fact]
  public void TheHasherListsEachFileSortedByTypeAndName() {
    var hashes = GamepackContentHasher.Compute(Pack())!;
    hashes.Files.Select(f => f.Type + ":" + f.Name).Should().Equal("command:Tap help", "image:help-icon", "sequence:Help All");
    hashes.ManifestSha256.Should().MatchRegex("^[0-9a-f]{64}$");
  }
}
