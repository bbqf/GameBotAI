using System.Net;
using System.Text;
using FluentAssertions;
using GameBot.UnitTests.Gamepacks;
using Xunit;

#pragma warning disable CA2007, CA1861, CA1707, CA2000

namespace GameBot.ContractTests.Gamepacks;

/// <summary>Feature 143 (T014): image rules, name rules, and the request size limit (FR-019, FR-023).</summary>
public sealed class GamepackImageRulesContractTests {
  [Theory]
  [InlineData("text/plain", GamepackTestKit.PngBase64)]
  [InlineData("image/png", "!!!not-base64!!!")]
  public async Task ABadImageGivesAStructureProblemInInstallAndDryRun(string contentType, string data) {
    using var host = new GamepackContractHost();
    var json = GamepackTestKit.Pack("p", "1", images: new[] { GamepackTestKit.Img("img", data, contentType) });

    var install = await host.InstallAsync(json);
    install.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    GamepackContractHost.ProblemCategories(await GamepackContractHost.ReadAsync(install)).Should().Equal("structure");

    var dry = await host.InstallAsync(json, dryRun: true);
    dry.StatusCode.Should().Be(HttpStatusCode.OK);
    var body = await GamepackContractHost.ReadAsync(dry);
    body.GetProperty("valid").GetBoolean().Should().BeFalse();
    GamepackContractHost.ProblemCategories(body).Should().Equal("structure");
  }

  [Fact]
  public async Task AnImageOverTenMillionBytesGivesAStructureProblem() {
    using var host = new GamepackContractHost();
    var json = GamepackTestKit.Pack("p", "1", images: new[] { GamepackTestKit.Img("big", Convert.ToBase64String(new byte[10_000_001])) });

    var response = await host.InstallAsync(json);

    response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    GamepackContractHost.ProblemCategories(await GamepackContractHost.ReadAsync(response)).Should().Equal("structure");
    (await host.GetJsonAsync("/api/gamepacks")).GetArrayLength().Should().Be(0);
  }

  [Fact]
  public async Task ARequestOverOneHundredMegabytesGives413AndChangesNothing() {
    using var host = new GamepackContractHost();
    var response = await host.Client.PostAsync(new Uri("/api/gamepacks/install", UriKind.Relative), new FakeLengthContent(100L * 1024 * 1024 + 1));

    response.StatusCode.Should().Be(HttpStatusCode.RequestEntityTooLarge);
    response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
    (await host.GetJsonAsync("/api/gamepacks")).GetArrayLength().Should().Be(0);
  }

  [Theory]
  [InlineData("CON")]
  [InlineData("nul.txt")]
  [InlineData("COM1")]
  [InlineData("bad/name")]
  [InlineData(".hidden")]
  public async Task ABadGamepackNameGivesAStructureProblem(string name) {
    using var host = new GamepackContractHost();
    var response = await host.InstallAsync(GamepackTestKit.Pack(name, "1"));

    response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    var body = await GamepackContractHost.ReadAsync(response);
    GamepackContractHost.ProblemCategories(body).Should().Equal("structure");
    body.GetProperty("problems")[0].GetProperty("objectType").GetString().Should().Be("manifest");
  }

  [Fact]
  public async Task AValidMaximumLengthNameIsAccepted() {
    using var host = new GamepackContractHost();
    var response = await host.InstallAsync(GamepackTestKit.Pack(new string('a', 64), "1"));
    response.StatusCode.Should().Be(HttpStatusCode.OK);
  }

  [Fact]
  public async Task AManifestWithRequiredApiMajorIsStoredAndShownButNotChecked() {
    using var host = new GamepackContractHost();
    var json = "{\"manifest\":{\"name\":\"p\",\"version\":\"1\",\"requiredApiMajor\":99}}";
    var response = await host.InstallAsync(json);
    response.StatusCode.Should().Be(HttpStatusCode.OK);
    var list = await host.GetJsonAsync("/api/gamepacks");
    list[0].GetProperty("requiredApiMajor").GetInt32().Should().Be(99);
  }

  [Fact]
  public async Task TheBodyIsReadAsUtf8Text() {
    using var host = new GamepackContractHost();
    var content = new StringContent(GamepackTestKit.Pack("p", "1", changelog: "café"), Encoding.UTF8, "application/json");
    var response = await host.Client.PostAsync(new Uri("/api/gamepacks/install", UriKind.Relative), content);
    response.StatusCode.Should().Be(HttpStatusCode.OK);
    (await host.GetJsonAsync("/api/gamepacks"))[0].GetProperty("changelog").GetString().Should().Be("café");
  }
}
