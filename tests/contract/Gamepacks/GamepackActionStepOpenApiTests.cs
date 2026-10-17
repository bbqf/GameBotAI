using System;
using System.Net;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using Xunit;

#pragma warning disable CA2007, CA1861, CA1707, CA2000, CA1305, CA1307, CA1310, CA1849

namespace GameBot.ContractTests.Gamepacks;

/// <summary>Feature 151 (FR-005): the install description has a sample Action step that runs a command.</summary>
public sealed class GamepackActionStepOpenApiTests {
  private static async Task<string> DescriptionAsync() {
    using var host = new GamepackContractHost();
    var response = await host.Client.GetAsync(new Uri("/swagger/v1/swagger.json", UriKind.Relative));
    response.StatusCode.Should().Be(HttpStatusCode.OK);
    using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
    return document.RootElement.GetProperty("paths").GetProperty("/api/gamepacks/install").GetProperty("post").GetProperty("description").GetString()!;
  }

  [Fact]
  public async Task TheInstallDescriptionHasASampleActionStep() {
    var description = await DescriptionAsync();

    description.Should().Contain("ACTION STEP");
    description.Should().Contain("\"stepType\":\"Action\"").And.Contain("\"primitiveAction\"").And.Contain("\"commandReference\"");
    description.Should().Contain("\"commands\":[");
  }

  [Fact]
  public async Task TheSampleInTheDescriptionInstalls() {
    var description = await DescriptionAsync();
    var start = description.IndexOf("Sample: ", StringComparison.Ordinal) + "Sample: ".Length;
    var end = description.LastIndexOf("}. ", StringComparison.Ordinal);
    var sample = description.Substring(start, end - start + 1);
    using var host = new GamepackContractHost();

    var response = await host.InstallAsync(sample, dryRun: true);

    var body = await GamepackContractHost.ReadAsync(response);
    body.GetProperty("valid").GetBoolean().Should().BeTrue(body.GetRawText());
  }
}
