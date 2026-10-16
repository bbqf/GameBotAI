using System;
using System.IO;
using System.Linq;
using FluentAssertions;
using GameBot.Domain.Gamepacks.Signing;
using Xunit;

#pragma warning disable CA2007, CA1861, CA1707, CA2000

namespace GameBot.UnitTests.Gamepacks.Signing;

/// <summary>Feature 146 (T009): the settings store. FR-004 and FR-013.</summary>
public sealed class GamepackSignatureSettingsStoreTests : IDisposable {
  private readonly string _root = Path.Combine(Path.GetTempPath(), "GameBotSigning", Guid.NewGuid().ToString("N"));

  public GamepackSignatureSettingsStoreTests() => Directory.CreateDirectory(_root);

  public void Dispose() {
    try {
      Directory.Delete(_root, recursive: true);
    }
    catch (IOException) {
      // A temporary folder that stays is not a test failure.
    }
  }

  [Fact]
  public void DefaultIsOffWithNoKeysWhenTheFileIsAbsent() {
    var store = new GamepackSignatureSettingsStore(_root);
    store.GetPolicy().Should().Be(SignaturePolicy.Off);
    store.Load().Keys.Should().BeEmpty();
    File.Exists(store.FilePath).Should().BeFalse();
  }

  [Fact]
  public void AddListAndRemoveAKey() {
    using var signer = new TestSigner();
    var store = new GamepackSignatureSettingsStore(_root);

    var added = store.AddKey("Builder A", signer.PublicKey);
    added.Status.Should().Be(AddKeyStatus.Added);
    added.Key!.Fingerprint.Should().Be(signer.Fingerprint);
    store.Load().Keys.Select(k => k.Label).Should().Equal("Builder A");

    store.RemoveKey(signer.Fingerprint).Should().BeTrue();
    store.Load().Keys.Should().BeEmpty();
    store.RemoveKey(signer.Fingerprint).Should().BeFalse();
  }

  [Fact]
  public void ADuplicateFingerprintIsRefused() {
    using var signer = new TestSigner();
    var store = new GamepackSignatureSettingsStore(_root);
    store.AddKey("A", signer.PublicKey).Status.Should().Be(AddKeyStatus.Added);
    store.AddKey("Other label", signer.PublicKey).Status.Should().Be(AddKeyStatus.Duplicate);
    store.Load().Keys.Should().HaveCount(1);
  }

  [Theory]
  [InlineData("not base64 !!")]
  [InlineData("AAAA")]
  [InlineData("")]
  public void ABadKeyIsRefusedAndNothingIsSaved(string key) {
    var store = new GamepackSignatureSettingsStore(_root);
    store.AddKey("A", key).Status.Should().Be(AddKeyStatus.InvalidKey);
    File.Exists(store.FilePath).Should().BeFalse();
  }

  [Fact]
  public void AKeyOnAnotherCurveIsRefused() {
    using var other = System.Security.Cryptography.ECDsa.Create(System.Security.Cryptography.ECCurve.NamedCurves.nistP384);
    var store = new GamepackSignatureSettingsStore(_root);
    store.AddKey("A", Convert.ToBase64String(other.ExportSubjectPublicKeyInfo())).Status.Should().Be(AddKeyStatus.InvalidKey);
  }

  [Theory]
  [InlineData("")]
  [InlineData("   ")]
  public void ABadLabelIsRefused(string label) {
    using var signer = new TestSigner();
    var store = new GamepackSignatureSettingsStore(_root);
    store.AddKey(label, signer.PublicKey).Status.Should().Be(AddKeyStatus.InvalidLabel);
    store.AddKey(new string('x', 101), signer.PublicKey).Status.Should().Be(AddKeyStatus.InvalidLabel);
    File.Exists(store.FilePath).Should().BeFalse();
  }

  [Fact]
  public void ANewStoreOnTheSameRootKeepsThePolicyAndTheKeys() {
    using var signer = new TestSigner();
    var first = new GamepackSignatureSettingsStore(_root);
    first.SetPolicy(SignaturePolicy.Required);
    first.AddKey("A", signer.PublicKey);

    var second = new GamepackSignatureSettingsStore(_root);
    second.GetPolicy().Should().Be(SignaturePolicy.Required);
    second.Load().Keys.Single().Fingerprint.Should().Be(signer.Fingerprint);
    File.ReadAllText(second.FilePath).Should().Contain("\"required\"");
  }

  [Fact]
  public void AnOutsideChangeToTheFileIsReadOnTheNextCall() {
    var store = new GamepackSignatureSettingsStore(_root);
    store.GetPolicy().Should().Be(SignaturePolicy.Off);
    File.WriteAllText(store.FilePath, "{\"policy\":\"optional\",\"keys\":[]}");
    store.GetPolicy().Should().Be(SignaturePolicy.Optional);
  }

  [Fact]
  public void ADamagedFileFailsClosed() {
    var store = new GamepackSignatureSettingsStore(_root);
    File.WriteAllText(store.FilePath, "{ not json");
    store.GetPolicy().Should().Be(SignaturePolicy.Required);
    store.Load().Keys.Should().BeEmpty();
  }
}
