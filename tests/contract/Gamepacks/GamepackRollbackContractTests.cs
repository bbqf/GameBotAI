using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using GameBot.UnitTests.Gamepacks;
using Xunit;

#pragma warning disable CA2007, CA1861, CA1707, CA2000

namespace GameBot.ContractTests.Gamepacks;

/// <summary>Feature 143 (T036): the rollback route.</summary>
public sealed class GamepackRollbackContractTests {
  private static string V1() => GamepackTestKit.Pack("pns-daily", "1.1.0",
    sequences: new[] { GamepackTestKit.Seq("s1", "Seq One"), GamepackTestKit.Seq("s2", "Seq Two") });

  private static string V2() => GamepackTestKit.Pack("pns-daily", "1.2.0",
    sequences: new[] { GamepackTestKit.Seq("s1", "Seq One"), GamepackTestKit.Seq("s3", "Seq Three") });

  [Fact]
  public async Task ARollbackGives200WithCountsAndClearsThePreviousVersion() {
    using var host = new GamepackContractHost();
    (await host.InstallAsync(V1())).StatusCode.Should().Be(HttpStatusCode.OK);
    (await host.InstallAsync(V2())).StatusCode.Should().Be(HttpStatusCode.OK);

    var response = await host.RollbackAsync("pns-daily");

    response.StatusCode.Should().Be(HttpStatusCode.OK);
    var body = await GamepackContractHost.ReadAsync(response);
    body.GetProperty("name").GetString().Should().Be("pns-daily");
    body.GetProperty("version").GetString().Should().Be("1.1.0");
    body.GetProperty("result").GetString().Should().Be("rolled_back");
    body.GetProperty("created").GetInt32().Should().Be(1);
    body.GetProperty("updated").GetInt32().Should().Be(1);
    body.GetProperty("removed").GetInt32().Should().Be(1);

    var list = await host.GetJsonAsync("/api/gamepacks");
    list[0].GetProperty("version").GetString().Should().Be("1.1.0");
    list[0].GetProperty("hasPreviousVersion").GetBoolean().Should().BeFalse();
  }

  [Fact]
  public async Task ASecondRollbackGives409() {
    using var host = new GamepackContractHost();
    (await host.InstallAsync(V1())).StatusCode.Should().Be(HttpStatusCode.OK);
    (await host.InstallAsync(V2())).StatusCode.Should().Be(HttpStatusCode.OK);
    (await host.RollbackAsync("pns-daily")).StatusCode.Should().Be(HttpStatusCode.OK);

    var second = await host.RollbackAsync("PNS-DAILY");

    second.StatusCode.Should().Be(HttpStatusCode.Conflict);
    (await GamepackContractHost.ReadAsync(second)).GetProperty("result").GetString().Should().Be("no_previous_version");
  }

  [Fact]
  public async Task AGamepackInstalledOnceHasNoPreviousVersion() {
    using var host = new GamepackContractHost();
    (await host.InstallAsync(V1())).StatusCode.Should().Be(HttpStatusCode.OK);
    var response = await host.RollbackAsync("pns-daily");
    response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    (await GamepackContractHost.ReadAsync(response)).GetProperty("result").GetString().Should().Be("no_previous_version");
  }

  [Fact]
  public async Task AnUnknownNameGives404() {
    using var host = new GamepackContractHost();
    var response = await host.RollbackAsync("nobody");
    response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    (await GamepackContractHost.ReadAsync(response)).GetProperty("result").GetString().Should().Be("not_found");
  }

  [Fact]
  public async Task ARefusedRollbackGives422WithTheProblemListAndChangesNothing() {
    using var host = new GamepackContractHost();
    (await host.InstallAsync(V1())).StatusCode.Should().Be(HttpStatusCode.OK);
    (await host.InstallAsync(V2())).StatusCode.Should().Be(HttpStatusCode.OK);
    // Version 1 needs the name "Seq Two". A user makes an object with this name now.
    (await host.Client.PostAsJsonAsync(new Uri("/api/sequences", UriKind.Relative), new { name = "Seq Two" })).StatusCode.Should().Be(HttpStatusCode.Created);

    var response = await host.RollbackAsync("pns-daily");

    response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    var body = await GamepackContractHost.ReadAsync(response);
    body.GetProperty("result").GetString().Should().Be("refused");
    body.GetProperty("valid").GetBoolean().Should().BeFalse();
    body.GetProperty("problems")[0].GetProperty("category").GetString().Should().Be("conflict");
    (await host.GetJsonAsync("/api/gamepacks"))[0].GetProperty("version").GetString().Should().Be("1.2.0");
  }

  [Fact]
  public async Task AWriteFailureGives500AndAFailedRestoreGivesPartialRestore() {
    using var host = new GamepackContractHost(withThrowingGate: true);
    (await host.InstallAsync(V1())).StatusCode.Should().Be(HttpStatusCode.OK);
    (await host.InstallAsync(V2())).StatusCode.Should().Be(HttpStatusCode.OK);

    host.Gate.Arm(1);
    var failed = await host.RollbackAsync("pns-daily");
    failed.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
    (await GamepackContractHost.ReadAsync(failed)).GetProperty("result").GetString().Should().Be("failed");
    (await host.GetJsonAsync("/api/gamepacks"))[0].GetProperty("version").GetString().Should().Be("1.2.0");

    host.Gate.Arm(2);
    host.Gate.ThrowOnRestore = true;
    var partial = await host.RollbackAsync("pns-daily");
    partial.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
    (await GamepackContractHost.ReadAsync(partial)).GetProperty("result").GetString().Should().Be("partial_restore");
  }
}
