using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using GameBot.UnitTests.Gamepacks;
using Xunit;

#pragma warning disable CA2007, CA1861, CA1707, CA2000, CA1305, CA1307, CA1310, CA1849

namespace GameBot.ContractTests.Gamepacks;

/// <summary>Feature 161: the failure explanation in the install answer (summary, take-over field, problem fields).</summary>
public sealed class GamepackInstallFailureExplanationContractTests {
  private const int ConflictCount = 20;

  private static string ManyConflictsPack(string name = "pns-many") => GamepackTestKit.Pack(name, "1.0.0",
    sequences: Enumerable.Range(0, ConflictCount).Select(i => GamepackTestKit.Seq("s" + i, "Seq " + i)).ToArray());

  private static async Task MakeManyUserSequencesAsync(GamepackContractHost host) {
    for (var i = 0; i < ConflictCount; i++) {
      (await host.Client.PostAsJsonAsync(new Uri("/api/sequences", UriKind.Relative), new { name = "Seq " + i })).StatusCode.Should().Be(HttpStatusCode.Created);
    }
  }

  private static string MixedPack() => GamepackTestKit.Pack("pns-mixed", "1.0.0",
    sequences: new[] { GamepackTestKit.Seq("a1", "Help All", "missing-cmd") },
    commands: new[] { GamepackTestKit.Cmd("c1", "Tap help") });

  private static IEnumerable<string> PropertyNames(JsonElement element) => element.EnumerateObject().Select(p => p.Name);

  [Fact]
  public async Task TheSummaryComesBeforeTheProblemsAndCountsTheConflicts() {
    using var host = new GamepackContractHost();
    await MakeManyUserSequencesAsync(host);

    var response = await host.InstallAsync(ManyConflictsPack(), dryRun: true);

    response.StatusCode.Should().Be(HttpStatusCode.OK);
    var body = await GamepackContractHost.ReadAsync(response);
    var names = PropertyNames(body).ToList();
    names.IndexOf("summary").Should().BeGreaterThan(-1).And.BeLessThan(names.IndexOf("problems"));
    var entry = body.GetProperty("summary").GetProperty("entries").EnumerateArray().Single();
    entry.GetProperty("category").GetString().Should().Be("conflict");
    entry.GetProperty("count").GetInt32().Should().Be(ConflictCount);
    entry.GetProperty("remedy").GetString().Should().Contain("take-over");
    entry.GetProperty("resolvedByTakeOver").GetBoolean().Should().BeTrue();
    body.GetProperty("wouldInstallWithTakeOver").GetBoolean().Should().BeTrue();
  }

  [Fact]
  public async Task AConflictAndAMissingReferenceGiveTwoEntriesAndFalse() {
    using var host = new GamepackContractHost();
    (await host.Client.PostAsJsonAsync(new Uri("/api/commands", UriKind.Relative), new { name = "Tap help", steps = Array.Empty<object>() }))
      .StatusCode.Should().Be(HttpStatusCode.Created);

    var response = await host.InstallAsync(MixedPack(), dryRun: true);

    var body = await GamepackContractHost.ReadAsync(response);
    body.GetProperty("summary").GetProperty("entries").EnumerateArray().Select(e => e.GetProperty("category").GetString())
      .Should().Equal("reference", "conflict");
    body.GetProperty("wouldInstallWithTakeOver").GetBoolean().Should().BeFalse();
  }

  [Fact]
  public async Task AValidFileGivesNoProblemsAndTrue() {
    using var host = new GamepackContractHost();

    var response = await host.InstallAsync(TakeOverContractKit.Pack(), dryRun: true);

    var body = await GamepackContractHost.ReadAsync(response);
    body.GetProperty("summary").GetProperty("headline").GetString().Should().Be("No problems.");
    body.GetProperty("summary").GetProperty("entries").GetArrayLength().Should().Be(0);
    body.GetProperty("wouldInstallWithTakeOver").GetBoolean().Should().BeTrue();
  }

  [Fact]
  public async Task AUserConflictResolvesAndOtherProblemsDoNot() {
    using var host = new GamepackContractHost();
    (await host.Client.PostAsJsonAsync(new Uri("/api/commands", UriKind.Relative), new { name = "Tap help", steps = Array.Empty<object>() }))
      .StatusCode.Should().Be(HttpStatusCode.Created);

    var body = await GamepackContractHost.ReadAsync(await host.InstallAsync(MixedPack(), dryRun: true));

    var problems = body.GetProperty("problems").EnumerateArray().ToList();
    problems.Single(p => p.GetProperty("category").GetString() == "conflict").GetProperty("takeOverResolves").GetBoolean().Should().BeTrue();
    var reference = problems.Single(p => p.GetProperty("category").GetString() == "reference");
    reference.GetProperty("takeOverResolves").GetBoolean().Should().BeFalse();
    problems.Should().OnlyContain(p => !string.IsNullOrWhiteSpace(p.GetProperty("remedy").GetString()));
  }

  [Fact]
  public async Task AConflictWithAnotherGamepackIsNotResolved() {
    using var host = new GamepackContractHost();
    (await host.InstallAsync(TakeOverContractKit.Pack("other", "1.0.0"))).StatusCode.Should().Be(HttpStatusCode.OK);

    var body = await GamepackContractHost.ReadAsync(await host.InstallAsync(TakeOverContractKit.Pack("pns-daily"), dryRun: true));

    body.GetProperty("problems").EnumerateArray().Should().OnlyContain(p => !p.GetProperty("takeOverResolves").GetBoolean());
    body.GetProperty("wouldInstallWithTakeOver").GetBoolean().Should().BeFalse();
  }

  [Fact]
  public async Task WithTakeOverOnEachRemainingProblemGivesFalse() {
    using var host = new GamepackContractHost();

    var body = await GamepackContractHost.ReadAsync(await host.InstallAsync(MixedPack(), dryRun: true, takeOver: true));

    body.GetProperty("problems").GetArrayLength().Should().BeGreaterThan(0);
    body.GetProperty("problems").EnumerateArray().Should().OnlyContain(p => !p.GetProperty("takeOverResolves").GetBoolean());
    body.GetProperty("wouldInstallWithTakeOver").GetBoolean().Should().BeFalse();
  }

  [Fact]
  public async Task ADryRunAndARealInstallGiveTheSameExplanationAndTheRealInstallChangesNothing() {
    using var host = new GamepackContractHost();
    await MakeManyUserSequencesAsync(host);
    var before = await host.StateSnapshotAsync();

    var dry = await GamepackContractHost.ReadAsync(await host.InstallAsync(ManyConflictsPack(), dryRun: true));
    var realResponse = await host.InstallAsync(ManyConflictsPack());
    var real = await GamepackContractHost.ReadAsync(realResponse);

    realResponse.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    real.GetProperty("summary").GetRawText().Should().Be(dry.GetProperty("summary").GetRawText());
    real.GetProperty("problems").GetRawText().Should().Be(dry.GetProperty("problems").GetRawText());
    real.GetProperty("wouldInstallWithTakeOver").GetBoolean().Should().Be(dry.GetProperty("wouldInstallWithTakeOver").GetBoolean());
    (await host.StateSnapshotAsync()).Should().Be(before);
  }

  [Fact]
  public async Task TheCatalogInstallRouteGivesTheSameExplanation() {
    using var host = new GamepackContractHost();
    var folder = System.IO.Path.Combine(host.Root, "catalog");
    new CatalogFixture().Add("pns-many", "1.0.0", ManyConflictsPack()).WriteTo(folder);
    await host.SetCatalogAsync(folder);
    await MakeManyUserSequencesAsync(host);

    var dryResponse = await host.CatalogInstallAsync("pns-many", "1.0.0", dryRun: true);
    var realResponse = await host.CatalogInstallAsync("pns-many", "1.0.0");

    dryResponse.StatusCode.Should().Be(HttpStatusCode.OK);
    realResponse.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    var dry = await GamepackContractHost.ReadAsync(dryResponse);
    var real = await GamepackContractHost.ReadAsync(realResponse);
    real.GetProperty("summary").GetRawText().Should().Be(dry.GetProperty("summary").GetRawText());
    real.GetProperty("summary").GetProperty("entries").EnumerateArray().Single().GetProperty("count").GetInt32().Should().Be(ConflictCount);
    real.GetProperty("wouldInstallWithTakeOver").GetBoolean().Should().BeTrue();
  }

  [Fact]
  public async Task TheRollbackRefusedAnswerHasTheProblemFieldsAndNoSummary() {
    using var host = new GamepackContractHost();
    var v1 = GamepackTestKit.Pack("pns-daily", "1.1.0",
      sequences: new[] { GamepackTestKit.Seq("s1", "Seq One"), GamepackTestKit.Seq("s2", "Seq Two") });
    var v2 = GamepackTestKit.Pack("pns-daily", "1.2.0",
      sequences: new[] { GamepackTestKit.Seq("s1", "Seq One"), GamepackTestKit.Seq("s3", "Seq Three") });
    (await host.InstallAsync(v1)).StatusCode.Should().Be(HttpStatusCode.OK);
    (await host.InstallAsync(v2)).StatusCode.Should().Be(HttpStatusCode.OK);
    (await host.Client.PostAsJsonAsync(new Uri("/api/sequences", UriKind.Relative), new { name = "Seq Two" })).StatusCode.Should().Be(HttpStatusCode.Created);

    var rollback = await host.RollbackAsync("pns-daily");

    rollback.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    var body = await GamepackContractHost.ReadAsync(rollback);
    body.TryGetProperty("summary", out _).Should().BeFalse();
    body.GetProperty("problems").EnumerateArray().All(p => p.GetRawText().Contains("takeOverResolves") && p.GetRawText().Contains("remedy")).Should().BeTrue();
  }
}
