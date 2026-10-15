using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using GameBot.UnitTests.Gamepacks;
using Xunit;

#pragma warning disable CA2007, CA1861, CA1707, CA2000

namespace GameBot.ContractTests.Gamepacks;

/// <summary>Feature 143 (T013): the install route.</summary>
public sealed class GamepackInstallContractTests {
  private static string ValidPack(string version = "1.2.0") => GamepackTestKit.Pack("pns-daily", version,
    sequences: new[] { GamepackTestKit.Seq("a1", "Help All", "c1") },
    commands: new[] { GamepackTestKit.Cmd("c1", "Tap help", imageId: "help-icon") },
    images: new[] { GamepackTestKit.Img("help-icon") },
    changelog: "Fix help sequence.");

  [Fact]
  public async Task ValidInstallGives200WithTheContractShape() {
    using var host = new GamepackContractHost();
    var response = await host.InstallAsync(ValidPack());

    response.StatusCode.Should().Be(HttpStatusCode.OK);
    var body = await GamepackContractHost.ReadAsync(response);
    body.GetProperty("dryRun").GetBoolean().Should().BeFalse();
    body.GetProperty("valid").GetBoolean().Should().BeTrue();
    body.GetProperty("problems").GetArrayLength().Should().Be(0);
    body.GetProperty("gamepack").GetProperty("name").GetString().Should().Be("pns-daily");
    body.GetProperty("gamepack").GetProperty("version").GetString().Should().Be("1.2.0");
    body.GetProperty("created").GetInt32().Should().Be(3);
    body.GetProperty("updated").GetInt32().Should().Be(0);
    body.GetProperty("removed").GetInt32().Should().Be(0);
  }

  [Fact]
  public async Task AConflictGives422WithTheProblemListAndChangesNothing() {
    using var host = new GamepackContractHost();
    var make = await host.Client.PostAsJsonAsync(new Uri("/api/sequences", UriKind.Relative), new { name = "Help All" });
    make.StatusCode.Should().Be(HttpStatusCode.Created);

    var response = await host.InstallAsync(ValidPack());

    response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    var body = await GamepackContractHost.ReadAsync(response);
    body.GetProperty("valid").GetBoolean().Should().BeFalse();
    var problem = body.GetProperty("problems").EnumerateArray().Single();
    problem.GetProperty("category").GetString().Should().Be("conflict");
    problem.GetProperty("objectType").GetString().Should().Be("sequence");
    problem.GetProperty("name").GetString().Should().Be("Help All");
    problem.GetProperty("owner").GetString().Should().Be("user");
    problem.GetProperty("message").GetString().Should().NotBeNullOrEmpty();
    (await host.GetJsonAsync("/api/gamepacks")).GetArrayLength().Should().Be(0);
    (await host.GetJsonAsync("/api/commands")).GetArrayLength().Should().Be(0);
  }

  [Fact]
  public async Task InvalidJsonGives400WithAStructureProblem() {
    using var host = new GamepackContractHost();
    var response = await host.InstallAsync("{ not json");

    response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    var body = await GamepackContractHost.ReadAsync(response);
    GamepackContractHost.ProblemCategories(body).Should().Equal("structure");
  }

  [Fact]
  public async Task AWriteFailureGives500AndLeavesTheStateAsBefore() {
    using var host = new GamepackContractHost(withThrowingGate: true);
    host.Gate.Arm(2);

    var response = await host.InstallAsync(ValidPack());

    response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
    var body = await GamepackContractHost.ReadAsync(response);
    body.GetProperty("result").GetString().Should().Be("failed");
    (await host.GetJsonAsync("/api/gamepacks")).GetArrayLength().Should().Be(0);
    (await host.GetJsonAsync("/api/sequences")).GetArrayLength().Should().Be(0);
  }

  [Fact]
  public async Task AFailedRestoreGives500WithPartialRestore() {
    using var host = new GamepackContractHost(withThrowingGate: true);
    host.Gate.Arm(4);
    host.Gate.ThrowOnRestore = true;

    var response = await host.InstallAsync(ValidPack());

    response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
    var body = await GamepackContractHost.ReadAsync(response);
    body.GetProperty("result").GetString().Should().Be("partial_restore");
    body.GetProperty("title").GetString().Should().Contain("partly restored");
  }

  [Fact]
  public async Task TheDryRunQueryGives200EvenWhenTheFileHasProblems() {
    using var host = new GamepackContractHost();
    var response = await host.InstallAsync(GamepackTestKit.Pack("p", "1", sequences: new[] { GamepackTestKit.Seq("s", "S", "ghost") }), dryRun: true);

    response.StatusCode.Should().Be(HttpStatusCode.OK);
    var body = await GamepackContractHost.ReadAsync(response);
    body.GetProperty("dryRun").GetBoolean().Should().BeTrue();
    body.GetProperty("valid").GetBoolean().Should().BeFalse();
    GamepackContractHost.ProblemCategories(body).Should().Equal("reference");
  }
}
