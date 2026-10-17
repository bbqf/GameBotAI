using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using GameBot.UnitTests.Gamepacks;
using Xunit;

#pragma warning disable CA2007, CA1861, CA1707, CA2000

namespace GameBot.ContractTests.Gamepacks;

/// <summary>Feature 156: the route that removes the record of an installed gamepack.</summary>
public sealed class GamepackRemoveContractTests {
  private static string V1() => GamepackTestKit.Pack("pns-daily", "1.1.0",
    sequences: new[] { GamepackTestKit.Seq("s1", "Seq One"), GamepackTestKit.Seq("s2", "Seq Two") });

  private static string V2() => GamepackTestKit.Pack("pns-daily", "1.2.0",
    sequences: new[] { GamepackTestKit.Seq("s1", "Seq One"), GamepackTestKit.Seq("s3", "Seq Three") });

  private static string Other() => GamepackTestKit.Pack("other-pack", "3.0.0",
    sequences: new[] { GamepackTestKit.Seq("o1", "Other Seq") });

  [Fact]
  public async Task ARemoveGives204WithNoBodyAndTheListDoesNotShowThePack() {
    using var host = new GamepackContractHost();
    (await host.InstallAsync(V1())).StatusCode.Should().Be(HttpStatusCode.OK);

    var response = await host.RemoveAsync("pns-daily");

    response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    (await response.Content.ReadAsStringAsync()).Should().BeEmpty();
    (await host.GetJsonAsync("/api/gamepacks")).GetArrayLength().Should().Be(0);
    (await host.GetJsonAsync("/api/sequences")).GetArrayLength().Should().Be(2);
  }

  [Fact]
  public async Task ARemoveWorksAfterAllContentItemsWereDeletedByHand() {
    using var host = new GamepackContractHost();
    (await host.InstallAsync(V1())).StatusCode.Should().Be(HttpStatusCode.OK);
    foreach (var name in new[] { "Seq One", "Seq Two" }) {
      var id = await host.SequenceIdAsync(name);
      (await host.Client.DeleteAsync(new Uri("/api/sequences/" + id, UriKind.Relative))).IsSuccessStatusCode.Should().BeTrue();
    }

    (await host.RemoveAsync("pns-daily")).StatusCode.Should().Be(HttpStatusCode.NoContent);
    (await host.GetJsonAsync("/api/gamepacks")).GetArrayLength().Should().Be(0);
  }

  [Fact]
  public async Task TheOtherPackStaysUnchanged() {
    using var host = new GamepackContractHost();
    (await host.InstallAsync(V1())).StatusCode.Should().Be(HttpStatusCode.OK);
    (await host.InstallAsync(Other())).StatusCode.Should().Be(HttpStatusCode.OK);

    (await host.RemoveAsync("pns-daily")).StatusCode.Should().Be(HttpStatusCode.NoContent);

    var list = await host.GetJsonAsync("/api/gamepacks");
    list.GetArrayLength().Should().Be(1);
    list[0].GetProperty("name").GetString().Should().Be("other-pack");
    list[0].GetProperty("version").GetString().Should().Be("3.0.0");
  }

  [Fact]
  public async Task AnInstallOfTheSameNameAfterARemoveIsAFirstInstall() {
    using var host = new GamepackContractHost();
    (await host.InstallAsync(V1())).StatusCode.Should().Be(HttpStatusCode.OK);
    (await host.InstallAsync(V2())).StatusCode.Should().Be(HttpStatusCode.OK);
    (await host.RemoveAsync("pns-daily")).StatusCode.Should().Be(HttpStatusCode.NoContent);
    foreach (var name in new[] { "Seq One", "Seq Three" }) {
      var id = await host.SequenceIdAsync(name);
      (await host.Client.DeleteAsync(new Uri("/api/sequences/" + id, UriKind.Relative))).IsSuccessStatusCode.Should().BeTrue();
    }

    var install = await host.InstallAsync(V1());

    install.StatusCode.Should().Be(HttpStatusCode.OK);
    var body = await GamepackContractHost.ReadAsync(install);
    body.GetProperty("created").GetInt32().Should().Be(2);
    body.GetProperty("updated").GetInt32().Should().Be(0);
    body.GetProperty("removed").GetInt32().Should().Be(0);
    var list = await host.GetJsonAsync("/api/gamepacks");
    list[0].GetProperty("version").GetString().Should().Be("1.1.0");
    list[0].GetProperty("hasPreviousVersion").GetBoolean().Should().BeFalse();
  }

  [Fact]
  public async Task AnObjectOfTheRemovedPackFollowsTheUserObjectConflictRule() {
    using var host = new GamepackContractHost();
    (await host.InstallAsync(V1())).StatusCode.Should().Be(HttpStatusCode.OK);
    (await host.RemoveAsync("pns-daily")).StatusCode.Should().Be(HttpStatusCode.NoContent);

    var conflict = await host.InstallAsync(V1());
    conflict.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    GamepackContractHost.ProblemCategories(await GamepackContractHost.ReadAsync(conflict)).Should().Contain("conflict");

    var takeOver = await host.InstallAsync(V1(), takeOver: true);
    takeOver.StatusCode.Should().Be(HttpStatusCode.OK);
    (await GamepackContractHost.ReadAsync(takeOver)).GetProperty("takenOver").GetInt32().Should().Be(2);
  }

  [Fact]
  public async Task ARollbackAfterARemoveGives404() {
    using var host = new GamepackContractHost();
    (await host.InstallAsync(V1())).StatusCode.Should().Be(HttpStatusCode.OK);
    (await host.InstallAsync(V2())).StatusCode.Should().Be(HttpStatusCode.OK);
    (await host.RemoveAsync("pns-daily")).StatusCode.Should().Be(HttpStatusCode.NoContent);

    var response = await host.RollbackAsync("pns-daily");

    response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    (await GamepackContractHost.ReadAsync(response)).GetProperty("result").GetString().Should().Be("not_found");
  }

  [Fact]
  public async Task ARollbackOfAPackWithNoPreviousVersionStillGives409() {
    using var host = new GamepackContractHost();
    (await host.InstallAsync(V1())).StatusCode.Should().Be(HttpStatusCode.OK);

    var response = await host.RollbackAsync("pns-daily");

    response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    (await GamepackContractHost.ReadAsync(response)).GetProperty("result").GetString().Should().Be("no_previous_version");
  }

  [Fact]
  public async Task TheKeptPreviousVersionIsGoneAfterARemove() {
    using var host = new GamepackContractHost();
    (await host.InstallAsync(V1())).StatusCode.Should().Be(HttpStatusCode.OK);
    (await host.InstallAsync(V2())).StatusCode.Should().Be(HttpStatusCode.OK);
    var previous = Path.Combine(host.Root, "gamepacks", "previous", "pns-daily.json");
    File.Exists(previous).Should().BeTrue();

    (await host.RemoveAsync("pns-daily")).StatusCode.Should().Be(HttpStatusCode.NoContent);

    File.Exists(previous).Should().BeFalse();
  }

  [Fact]
  public async Task AnUnknownNameGives404WithNoDataChange() {
    using var host = new GamepackContractHost();
    (await host.InstallAsync(V1())).StatusCode.Should().Be(HttpStatusCode.OK);
    var before = await host.StateSnapshotAsync();

    var response = await host.RemoveAsync("nobody");

    response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    (await GamepackContractHost.ReadAsync(response)).GetProperty("result").GetString().Should().Be("not_found");
    (await host.StateSnapshotAsync()).Should().Be(before);
  }

  [Fact]
  public async Task ASecondRemoveGives404() {
    using var host = new GamepackContractHost();
    (await host.InstallAsync(V1())).StatusCode.Should().Be(HttpStatusCode.OK);
    (await host.RemoveAsync("pns-daily")).StatusCode.Should().Be(HttpStatusCode.NoContent);

    var second = await host.RemoveAsync("pns-daily");

    second.StatusCode.Should().Be(HttpStatusCode.NotFound);
    (await GamepackContractHost.ReadAsync(second)).GetProperty("result").GetString().Should().Be("not_found");
  }

  [Fact]
  public async Task ANameWithOtherLetterCaseMatchesTheRecord() {
    using var host = new GamepackContractHost();
    (await host.InstallAsync(V1())).StatusCode.Should().Be(HttpStatusCode.OK);

    (await host.RemoveAsync("PNS-Daily")).StatusCode.Should().Be(HttpStatusCode.NoContent);
    (await host.GetJsonAsync("/api/gamepacks")).GetArrayLength().Should().Be(0);
  }

  [Fact]
  public async Task AnInvalidNameGives404() {
    using var host = new GamepackContractHost();

    var response = await host.RemoveAsync("bad%20name%3A");

    response.StatusCode.Should().Be(HttpStatusCode.NotFound);
  }

  [Fact]
  public async Task TheDeleteOfAKeyKeepsItsOwnRoute() {
    using var host = new GamepackContractHost();
    (await host.InstallAsync(V1())).StatusCode.Should().Be(HttpStatusCode.OK);
    var fingerprint = new string('a', 64);

    var response = await host.Client.DeleteAsync(new Uri("/api/gamepacks/signature/keys/" + fingerprint, UriKind.Relative));

    // The key route answers. It is not the remove route, so the pack stays.
    response.StatusCode.Should().NotBe(HttpStatusCode.NoContent).And.NotBe(HttpStatusCode.MethodNotAllowed);
    (await host.GetJsonAsync("/api/gamepacks")).GetArrayLength().Should().Be(1);
  }
}
