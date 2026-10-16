using System;
using System.Net;
using System.Net.Http.Json;
using System.Threading.Tasks;
using FluentAssertions;
using Xunit;

#pragma warning disable CA2007, CA1861, CA1707, CA2000

namespace GameBot.ContractTests.Gamepacks;

/// <summary>Feature 146 (T026): the policy routes.</summary>
public sealed class GamepackSignaturePolicyContractTests {
  private static readonly Uri PolicyUri = new("/api/gamepacks/signature/policy", UriKind.Relative);

  [Fact]
  public async Task TheDefaultPolicyIsOff() {
    using var host = new GamepackContractHost();
    var body = await host.GetJsonAsync("/api/gamepacks/signature/policy");
    body.GetProperty("policy").GetString().Should().Be("off");
  }

  [Theory]
  [InlineData("required")]
  [InlineData("optional")]
  [InlineData("off")]
  public async Task APolicyCanBeSetAndRead(string policy) {
    using var host = new GamepackContractHost();
    var put = await host.Client.PutAsJsonAsync(PolicyUri, new { policy });
    put.StatusCode.Should().Be(HttpStatusCode.OK);
    (await GamepackContractHost.ReadAsync(put)).GetProperty("policy").GetString().Should().Be(policy);
    (await host.GetJsonAsync("/api/gamepacks/signature/policy")).GetProperty("policy").GetString().Should().Be(policy);
  }

  [Theory]
  [InlineData("strict")]
  [InlineData("")]
  public async Task AnUnknownPolicyGives400AndKeepsTheOldPolicy(string policy) {
    using var host = new GamepackContractHost();
    await host.SetPolicyAsync("optional");
    var put = await host.Client.PutAsJsonAsync(PolicyUri, new { policy });
    put.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    (await host.GetJsonAsync("/api/gamepacks/signature/policy")).GetProperty("policy").GetString().Should().Be("optional");
  }
}
