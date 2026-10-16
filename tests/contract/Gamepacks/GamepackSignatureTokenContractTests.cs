using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;
using FluentAssertions;
using Xunit;

#pragma warning disable CA2007, CA1861, CA1707, CA2000

namespace GameBot.ContractTests.Gamepacks;

/// <summary>Feature 146 (T028, clarification A2): each new settings route uses the API token rule.</summary>
public sealed class GamepackSignatureTokenContractTests {
  [Theory]
  [InlineData("GET", "/api/gamepacks/signature/policy")]
  [InlineData("PUT", "/api/gamepacks/signature/policy")]
  [InlineData("GET", "/api/gamepacks/signature/keys")]
  [InlineData("POST", "/api/gamepacks/signature/keys")]
  [InlineData("DELETE", "/api/gamepacks/signature/keys/abc")]
  public async Task ACallWithNoTokenIsRefused(string method, string path) {
    using var host = new GamepackContractHost();
    using var anonymous = host.CreateAnonymousClient();
    using var request = new HttpRequestMessage(new HttpMethod(method), new Uri(path, UriKind.Relative));
    if (method is "PUT" or "POST") {
      request.Content = JsonContent.Create(new { policy = "off" });
    }

    var response = await anonymous.SendAsync(request);

    response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
  }
}
