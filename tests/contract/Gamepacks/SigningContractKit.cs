using System;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using GameBot.UnitTests.Gamepacks;
using GameBot.UnitTests.Gamepacks.Signing;

#pragma warning disable CA2007, CA1861, CA1707, CA2000

namespace GameBot.ContractTests.Gamepacks;

/// <summary>Shared helpers for the signature contract tests (feature 146).</summary>
internal static class SigningContractKit {
  /// <summary>A small valid gamepack. The sequence name changes the content.</summary>
  public static string Pack(string sequenceName = "Help All", string version = "1.0.0") => GamepackTestKit.Pack("pns-daily", version,
    sequences: new[] { GamepackTestKit.Seq("a1", sequenceName, "c1") },
    commands: new[] { GamepackTestKit.Cmd("c1", "Tap help", imageId: "help-icon") },
    images: new[] { GamepackTestKit.Img("help-icon") });

  /// <summary>Sets the policy and trusts the key of the signer.</summary>
  public static async Task ConfigureAsync(GamepackContractHost host, string policy, TestSigner? trusted) {
    await host.SetPolicyAsync(policy);
    if (trusted is not null) {
      await host.AddKeyAsync("Builder", trusted.PublicKey);
    }
  }

  /// <summary>The four bad cases: the signature text and the gamepack to send, with the expected status.</summary>
  public static (string Status, string Pack, string? Signature) BadCase(string status, TestSigner trusted, TestSigner stranger) {
    var pack = Pack();
    return status switch {
      "missing" => (status, pack, null),
      "invalid" => (status, pack, trusted.SignWithBadSignature(pack)),
      "content_mismatch" => (status, Pack("Help All Changed"), trusted.Sign(pack)),
      "untrusted" => (status, pack, stranger.Sign(pack)),
      _ => throw new ArgumentOutOfRangeException(nameof(status))
    };
  }

  public static async Task<JsonElement> SignatureBlockAsync(HttpResponseMessage response) =>
    (await GamepackContractHost.ReadAsync(response)).GetProperty("signature");

  public static void ShouldBeStatus(this JsonElement block, string status, string policy, bool wouldReject) {
    block.GetProperty("status").GetString().Should().Be(status);
    block.GetProperty("policy").GetString().Should().Be(policy);
    block.GetProperty("wouldReject").GetBoolean().Should().Be(wouldReject);
  }

  public static void ShouldBe(this HttpResponseMessage response, HttpStatusCode code) => response.StatusCode.Should().Be(code);
}
