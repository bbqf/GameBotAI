using System.Linq;
using FluentAssertions;
using GameBot.Domain.Gamepacks;
using GameBot.Domain.Gamepacks.Signing;
using Xunit;

#pragma warning disable CA1707, CA1861, CA1307

namespace GameBot.UnitTests.Gamepacks.Signing;

/// <summary>Feature 150 (T031, FR-017): the signature covers the seed templates and a file without seeds keeps its hash.</summary>
public sealed class GamepackContentHasherSeedTests {
  // A fixed file without seeds. The expected values were computed outside the code (SHA-256 of the canonical JSON), so they do not depend on the hasher.
  private const string FixedPack = "{\"manifest\":{\"name\":\"pns\",\"version\":\"1\"},\"sequences\":[{\"id\":\"a1\",\"name\":\"Help All\"}],\"commands\":[],\"images\":[]}";

  private static string Seeded() => GamepackTestKit.Pack("p", "1",
    sequences: new[] { GamepackTestKit.Seq("a1", "Help All") },
    seedTemplates: new[] { GamepackTestKit.Seed("Daily", GamepackTestKit.SeedEntry("Help All")) });

  [Fact]
  public void AFileWithoutSeedsKeepsItsHashAndItsFileList() {
    var hashes = GamepackContentHasher.Compute(FixedPack)!;

    hashes.ManifestSha256.Should().Be(ExpectedManifestHash);
    hashes.Files.Should().ContainSingle();
    hashes.Files[0].Type.Should().Be(GamepackObjectTypes.Sequence);
    hashes.Files[0].Sha256.Should().Be(ExpectedSequenceHash);
  }

  [Fact]
  public void AnEmptySeedListAddsNoEntry() {
    var withEmpty = FixedPack.Replace("\"images\":[]", "\"images\":[],\"seedTemplates\":[]");

    GamepackContentHasher.Compute(withEmpty)!.Files.Should().BeEquivalentTo(GamepackContentHasher.Compute(FixedPack)!.Files);
  }

  [Fact]
  public void ASeedAddsASeedTemplateEntryWithTheNameAndAHash() {
    var hashes = GamepackContentHasher.Compute(Seeded())!;

    var entry = hashes.Files.Single(f => f.Type == GamepackObjectTypes.SeedTemplate);
    entry.Name.Should().Be("Daily");
    entry.Sha256.Should().MatchRegex("^[0-9a-f]{64}$");
  }

  [Fact]
  public void ASignedFileThatGetsASeedAfterSigningFailsTheCheck() {
    using var signer = new TestSigner();
    var signed = GamepackTestKit.Pack("p", "1", sequences: new[] { GamepackTestKit.Seq("a1", "Help All") });
    var signature = signer.Sign(signed);

    var tampered = Seeded();
    var verifier = new GamepackSignatureVerifier();
    var good = verifier.Verify(signed, signature, new[] { signer.AsTrusted() });
    var bad = verifier.Verify(tampered, signature, new[] { signer.AsTrusted() });

    good.Should().Be(SignatureStatus.Valid);
    bad.Should().NotBe(SignatureStatus.Valid);
  }

  [Fact]
  public void ASignedSeedEntryIsValidAndAChangedSeedIsNot() {
    using var signer = new TestSigner();
    var text = Seeded();
    var signature = signer.Sign(text);
    var verifier = new GamepackSignatureVerifier();

    verifier.Verify(text, signature, new[] { signer.AsTrusted() }).Should().Be(SignatureStatus.Valid);
    verifier.Verify(text.Replace("Daily", "Other"), signature, new[] { signer.AsTrusted() }).Should().NotBe(SignatureStatus.Valid);
  }

  private const string ExpectedManifestHash = "f78448008bcbf12ab17181da7c1bd4b04b05e4fadaea84a8dfed9b84968b8b0c";
  private const string ExpectedSequenceHash = "fe6c20e9dbaed3351edd31f7ad9119f1eeb678f64055653cb5d8dcdb97433847";
}
