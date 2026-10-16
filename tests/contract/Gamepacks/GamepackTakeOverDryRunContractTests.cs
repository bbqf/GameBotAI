using System.Linq;
using System.Net;
using System.Threading.Tasks;
using FluentAssertions;
using Xunit;

#pragma warning disable CA2007, CA1861, CA1707, CA2000, CA1305, CA1307, CA1310, CA1849

namespace GameBot.ContractTests.Gamepacks;

/// <summary>Feature 149 (T037): the dry run with the option takeOver.</summary>
public sealed class GamepackTakeOverDryRunContractTests {
  [Fact]
  public async Task ADryRunListsTheObjectsWithMarksAndChangesNothing() {
    using var host = new GamepackContractHost();
    await TakeOverContractKit.MakeUserObjectsAsync(host);
    var before = await host.StateSnapshotAsync();

    var response = await host.InstallAsync(TakeOverContractKit.Pack(), dryRun: true, takeOver: true);

    response.StatusCode.Should().Be(HttpStatusCode.OK);
    var body = await GamepackContractHost.ReadAsync(response);
    body.GetProperty("dryRun").GetBoolean().Should().BeTrue();
    body.GetProperty("valid").GetBoolean().Should().BeTrue();
    body.GetProperty("takenOver").GetInt32().Should().Be(2);
    body.TryGetProperty("takeOverBackupId", out _).Should().BeFalse();
    var list = body.GetProperty("takeOvers").EnumerateArray().ToList();
    list.Should().HaveCount(2);
    list.Select(e => e.GetProperty("type").GetString() + ":" + e.GetProperty("name").GetString()).Should().BeEquivalentTo("sequence:Help All", "command:Tap help");
    list.Should().OnlyContain(e => e.GetProperty("identical").ValueKind == System.Text.Json.JsonValueKind.False);
    (await host.StateSnapshotAsync()).Should().Be(before);
    TakeOverContractKit.BackupFileCount(host).Should().Be(0);
  }

  [Fact]
  public async Task ADryRunWithTheOptionOffReportsTheConflictAndHasNoList() {
    using var host = new GamepackContractHost();
    await TakeOverContractKit.MakeUserObjectsAsync(host);

    var response = await host.InstallAsync(TakeOverContractKit.Pack(), dryRun: true);

    response.StatusCode.Should().Be(HttpStatusCode.OK);
    var body = await GamepackContractHost.ReadAsync(response);
    body.GetProperty("valid").GetBoolean().Should().BeFalse();
    body.GetProperty("problems").GetArrayLength().Should().BeGreaterThan(0);
    body.TryGetProperty("takeOvers", out _).Should().BeFalse();
  }

  [Theory]
  [InlineData(false)]
  [InlineData(true)]
  public async Task ADryRunWithAConflictOfAnotherGamepackGives200WithValidFalseAndNoList(bool takeOver) {
    using var host = new GamepackContractHost();
    (await host.InstallAsync(TakeOverContractKit.Pack("other", "1.0.0"))).StatusCode.Should().Be(HttpStatusCode.OK);

    var response = await host.InstallAsync(TakeOverContractKit.Pack("pns-daily"), dryRun: true, takeOver: takeOver);

    response.StatusCode.Should().Be(HttpStatusCode.OK);
    var body = await GamepackContractHost.ReadAsync(response);
    body.GetProperty("valid").GetBoolean().Should().BeFalse();
    body.GetProperty("problems").GetArrayLength().Should().BeGreaterThan(0);
    body.TryGetProperty("takeOvers", out _).Should().BeFalse();
  }

  [Fact]
  public async Task ADryRunWithNothingToTakeOverHasAnEmptyList() {
    using var host = new GamepackContractHost();

    var response = await host.InstallAsync(TakeOverContractKit.Pack(), dryRun: true, takeOver: true);

    var body = await GamepackContractHost.ReadAsync(response);
    body.GetProperty("takenOver").GetInt32().Should().Be(0);
    body.GetProperty("takeOvers").GetArrayLength().Should().Be(0);
  }
}
