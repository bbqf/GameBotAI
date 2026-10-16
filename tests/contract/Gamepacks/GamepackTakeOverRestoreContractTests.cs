using System.Net;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

#pragma warning disable CA2007, CA1861, CA1707, CA2000, CA1305, CA1307, CA1310, CA1849

namespace GameBot.ContractTests.Gamepacks;

/// <summary>Feature 149 (T029): the restore route.</summary>
public sealed class GamepackTakeOverRestoreContractTests {
  private static async Task<string> InstallWithTakeOverAsync(GamepackContractHost host) {
    await TakeOverContractKit.MakeUserObjectsAsync(host);
    var response = await host.InstallAsync(TakeOverContractKit.Pack(), takeOver: true);
    response.StatusCode.Should().Be(HttpStatusCode.OK);
    return (await GamepackContractHost.ReadAsync(response)).GetProperty("takeOverBackupId").GetString()!;
  }

  [Fact]
  public async Task ARestoreGives200AndTheObjectsHaveTheirOldContent() {
    using var host = new GamepackContractHost();
    var backupId = await InstallWithTakeOverAsync(host);
    var sequenceId = await host.SequenceIdAsync("Help All");
    (await TakeOverContractKit.StepCountOfSequenceAsync(host, sequenceId)).Should().BeGreaterThan(0);

    var response = await host.RestoreTakeOverAsync(backupId);

    response.StatusCode.Should().Be(HttpStatusCode.OK);
    var body = await GamepackContractHost.ReadAsync(response);
    body.GetProperty("backupId").GetString().Should().Be(backupId);
    body.GetProperty("result").GetString().Should().Be("restored");
    body.GetProperty("restored").GetInt32().Should().Be(2);
    (await TakeOverContractKit.StepCountOfSequenceAsync(host, sequenceId)).Should().Be(0);
    (await TakeOverContractKit.OwnerKindOfSequenceAsync(host, sequenceId)).Should().Be("user");
  }

  [Theory]
  [InlineData("to-20200101T000000000-00000000")]
  [InlineData("bad-id")]
  [InlineData("..%5C..%5Cregistry")]
  public async Task AnUnknownOrMalformedIdGives404(string id) {
    using var host = new GamepackContractHost();

    var response = await host.RestoreTakeOverAsync(id);

    response.StatusCode.Should().Be(HttpStatusCode.NotFound);
  }

  [Fact]
  public async Task ARefusedRestoreGives409WithTheReasonsAndChangesNothing() {
    using var host = new GamepackContractHost();
    var backupId = await InstallWithTakeOverAsync(host);
    var commandId = await host.CommandIdAsync("Tap help");
    // A hand delete of a gamepack object is refused, so the test removes the object in the store.
    (await host.Services.GetRequiredService<GameBot.Domain.Commands.ICommandRepository>().DeleteAsync(commandId)).Should().BeTrue();
    var before = await host.StateSnapshotAsync();

    var response = await host.RestoreTakeOverAsync(backupId);

    response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    var body = await GamepackContractHost.ReadAsync(response);
    body.GetProperty("result").GetString().Should().Be("refused");
    TakeOverContractKit.ReasonCodes(body).Should().Contain("object_missing");
    body.GetProperty("reasons")[0].GetProperty("type").GetString().Should().NotBeNullOrEmpty();
    body.GetProperty("reasons")[0].GetProperty("name").GetString().Should().NotBeNullOrEmpty();
    body.GetProperty("reasons")[0].GetProperty("message").GetString().Should().NotBeNullOrEmpty();
    (await host.StateSnapshotAsync()).Should().Be(before);
  }

  [Fact]
  public async Task ASecondRestoreGives409WithOwnerChanged() {
    using var host = new GamepackContractHost();
    var backupId = await InstallWithTakeOverAsync(host);
    (await host.RestoreTakeOverAsync(backupId)).StatusCode.Should().Be(HttpStatusCode.OK);

    var second = await host.RestoreTakeOverAsync(backupId);

    second.StatusCode.Should().Be(HttpStatusCode.Conflict);
    TakeOverContractKit.ReasonCodes(await GamepackContractHost.ReadAsync(second)).Should().OnlyContain(c => c == "owner_changed");
  }

  [Fact]
  public async Task AFailedWriteGives500AndLeavesTheState() {
    using var host = new GamepackContractHost(withThrowingGate: true);
    var backupId = await InstallWithTakeOverAsync(host);
    var before = await host.StateSnapshotAsync();
    host.Gate.Arm(1);

    var response = await host.RestoreTakeOverAsync(backupId);

    response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
    (await GamepackContractHost.ReadAsync(response)).GetProperty("result").GetString().Should().Be("failed");
    (await host.StateSnapshotAsync()).Should().Be(before);
  }
}
