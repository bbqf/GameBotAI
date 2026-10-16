using System;
using System.IO;
using System.Net;
using System.Net.Http.Json;
using System.Threading.Tasks;
using FluentAssertions;
using GameBot.UnitTests.Gamepacks.Signing;
using Xunit;

#pragma warning disable CA2007, CA1861, CA1707, CA2000

namespace GameBot.ContractTests.Gamepacks;

/// <summary>Feature 146 (T027): the key routes.</summary>
public sealed class GamepackSignatureKeysContractTests {
  private static readonly Uri KeysUri = new("/api/gamepacks/signature/keys", UriKind.Relative);

  [Fact]
  public async Task AddListAndRemoveAKey() {
    using var host = new GamepackContractHost();
    using var signer = new TestSigner();

    var add = await host.Client.PostAsJsonAsync(KeysUri, new { label = "Builder A", publicKey = signer.PublicKey });
    add.StatusCode.Should().Be(HttpStatusCode.Created);
    var created = await GamepackContractHost.ReadAsync(add);
    created.GetProperty("label").GetString().Should().Be("Builder A");
    created.GetProperty("fingerprint").GetString().Should().Be(signer.Fingerprint);

    var list = await host.Client.GetAsync(KeysUri);
    var text = await list.Content.ReadAsStringAsync();
    text.Should().Contain(signer.Fingerprint).And.NotContain(signer.PublicKey).And.NotContainEquivalentOf("publicKey");

    var remove = await host.Client.DeleteAsync(new Uri(KeysUri + "/" + signer.Fingerprint, UriKind.Relative));
    remove.StatusCode.Should().Be(HttpStatusCode.NoContent);
    (await host.GetJsonAsync("/api/gamepacks/signature/keys")).GetArrayLength().Should().Be(0);
  }

  [Fact]
  public async Task ADuplicateKeyGives409() {
    using var host = new GamepackContractHost();
    using var signer = new TestSigner();
    await host.AddKeyAsync("A", signer.PublicKey);

    var second = await host.Client.PostAsJsonAsync(KeysUri, new { label = "B", publicKey = signer.PublicKey });

    second.StatusCode.Should().Be(HttpStatusCode.Conflict);
    (await host.GetJsonAsync("/api/gamepacks/signature/keys")).GetArrayLength().Should().Be(1);
  }

  [Theory]
  [InlineData("A", "not a key")]
  [InlineData("A", "")]
  [InlineData("", "KEY")]
  public async Task ABadKeyOrLabelGives400AndSavesNothing(string label, string key) {
    using var host = new GamepackContractHost();
    using var signer = new TestSigner();
    var publicKey = key == "KEY" ? signer.PublicKey : key;

    var response = await host.Client.PostAsJsonAsync(KeysUri, new { label, publicKey });

    response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    (await host.GetJsonAsync("/api/gamepacks/signature/keys")).GetArrayLength().Should().Be(0);
    File.Exists(Path.Combine(host.Root, "gamepack-signature.json")).Should().BeFalse();
  }

  [Fact]
  public async Task AnUnknownFingerprintGives404() {
    using var host = new GamepackContractHost();
    var response = await host.Client.DeleteAsync(new Uri(KeysUri + "/" + new string('a', 64), UriKind.Relative));
    response.StatusCode.Should().Be(HttpStatusCode.NotFound);
  }
}
