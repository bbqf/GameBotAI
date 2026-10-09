using FluentAssertions;
using GameBot.Domain.Updates;
using GameBot.Domain.Versioning;
using Xunit;

namespace GameBot.UnitTests.Updates;

public sealed class UpdateManifestTests {
  private static readonly SemanticVersion Version = new(1, 7, 0, 430);

  [Fact]
  public void ValidManifestParses() {
    var manifest = UpdateManifest.Parse(UpdateTestData.ManifestJson(), Version);

    manifest.Version.Should().Be(Version);
    manifest.MsiFileName.Should().Be("GameBot.msi");
    manifest.MsiSizeBytes.Should().Be(48211968);
    manifest.MsiSha256.Should().Be(UpdateTestData.Sha);
  }

  [Fact]
  public void UpperCaseChecksumIsStoredInLowerCase() {
    var manifest = UpdateManifest.Parse(UpdateTestData.ManifestJson(sha: UpdateTestData.Sha.ToUpperInvariant()), Version);

    manifest.MsiSha256.Should().Be(UpdateTestData.Sha);
  }

  [Fact]
  public void HigherSchemaIsUnsupported() {
    var act = () => UpdateManifest.Parse(UpdateTestData.ManifestJson(schema: 2), Version);

    act.Should().Throw<UpdateManifestException>().Which.Code.Should().Be("update_manifest_unsupported");
  }

  [Theory]
  [InlineData(0)]
  [InlineData(-1)]
  public void LowerSchemaIsInvalid(int schema) {
    var act = () => UpdateManifest.Parse(UpdateTestData.ManifestJson(schema: schema), Version);

    act.Should().Throw<UpdateManifestException>().Which.Code.Should().Be("update_manifest_invalid");
  }

  [Fact]
  public void VersionMustMatchTheTag() {
    var act = () => UpdateManifest.Parse(UpdateTestData.ManifestJson(version: "1.7.0.431"), Version);

    act.Should().Throw<UpdateManifestException>().Which.Code.Should().Be("update_manifest_invalid");
  }

  [Theory]
  [InlineData("abc")]
  [InlineData("")]
  public void BadHashLengthIsInvalid(string sha) {
    var act = () => UpdateManifest.Parse(UpdateTestData.ManifestJson(sha: sha), Version);

    act.Should().Throw<UpdateManifestException>();
  }

  [Theory]
  [InlineData(0)]
  [InlineData(-5)]
  public void SizeMustBeGreaterThanZero(long size) {
    var act = () => UpdateManifest.Parse(UpdateTestData.ManifestJson(size: size), Version);

    act.Should().Throw<UpdateManifestException>();
  }

  [Theory]
  [InlineData("not json")]
  [InlineData("[]")]
  [InlineData("{}")]
  [InlineData("{\"schemaVersion\":1,\"version\":\"bad\"}")]
  [InlineData("{\"schemaVersion\":1,\"version\":\"1.7.0.430\"}")]
  [InlineData("{\"schemaVersion\":1,\"version\":\"1.7.0.430\",\"msi\":{\"sizeBytes\":5,\"sha256\":\"x\"}}")]
  [InlineData("{\"schemaVersion\":1,\"version\":\"1.7.0.430\",\"msi\":{\"fileName\":\"a.msi\",\"sizeBytes\":\"5\"}}")]
  public void BrokenManifestIsInvalid(string json) {
    var act = () => UpdateManifest.Parse(json, Version);

    act.Should().Throw<UpdateManifestException>().Which.Code.Should().Be("update_manifest_invalid");
  }

  [Fact]
  public void ExceptionConstructorsKeepTheCode() {
    new UpdateManifestException().Code.Should().Be("update_manifest_invalid");
    new UpdateManifestException("m").Message.Should().Be("m");
    new UpdateManifestException("m", new InvalidOperationException()).InnerException.Should().NotBeNull();
    new UpdateManifestException("c", "m").Code.Should().Be("c");
  }
}
