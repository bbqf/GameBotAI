using System;
using System.Net;
using System.Net.Http.Json;
using System.Threading.Tasks;
using FluentAssertions;
using Xunit;

#pragma warning disable CA2007, CA1861, CA1707, CA2000, CA1305, CA1307, CA1310, CA1849

namespace GameBot.ContractTests.Gamepacks;

/// <summary>Feature 149 (T015): the install route with the option takeOver. A conflict gives 422, never 409.</summary>
public sealed class GamepackTakeOverInstallContractTests {
  [Fact]
  public async Task TheOptionOffKeepsTheUserConflictAnd422() {
    using var host = new GamepackContractHost();
    await TakeOverContractKit.MakeUserObjectsAsync(host);

    var response = await host.InstallAsync(TakeOverContractKit.Pack());

    response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    var body = await GamepackContractHost.ReadAsync(response);
    body.GetProperty("valid").GetBoolean().Should().BeFalse();
    body.GetProperty("takenOver").GetInt32().Should().Be(0);
    body.TryGetProperty("takeOverBackupId", out _).Should().BeFalse();
    (await host.GetJsonAsync("/api/gamepacks")).GetArrayLength().Should().Be(0);
  }

  [Fact]
  public async Task TheOptionOnTakesTheObjectsOverAndGives200WithTheCount() {
    using var host = new GamepackContractHost();
    await TakeOverContractKit.MakeUserObjectsAsync(host);
    var sequenceId = await host.SequenceIdAsync("Help All");
    var commandId = await host.CommandIdAsync("Tap help");

    var response = await host.InstallAsync(TakeOverContractKit.Pack(), takeOver: true);

    response.StatusCode.Should().Be(HttpStatusCode.OK);
    var body = await GamepackContractHost.ReadAsync(response);
    body.GetProperty("valid").GetBoolean().Should().BeTrue();
    body.GetProperty("takenOver").GetInt32().Should().Be(2);
    body.GetProperty("created").GetInt32().Should().Be(1);
    body.GetProperty("updated").GetInt32().Should().Be(0);
    body.GetProperty("takeOverBackupId").GetString().Should().MatchRegex(TakeOverContractKit.BackupIdPattern);
    body.TryGetProperty("takeOvers", out _).Should().BeFalse("a real install has no list");
    (await host.SequenceIdAsync("Help All")).Should().Be(sequenceId);
    (await host.CommandIdAsync("Tap help")).Should().Be(commandId);
    (await TakeOverContractKit.OwnerKindOfSequenceAsync(host, sequenceId)).Should().Be("gamepack");
    TakeOverContractKit.BackupFileCount(host).Should().Be(1);
  }

  [Theory]
  [InlineData(false)]
  [InlineData(true)]
  public async Task AConflictWithAnotherGamepackGives422WithTheOptionOnAndOffAndNever409(bool takeOver) {
    using var host = new GamepackContractHost();
    (await host.InstallAsync(TakeOverContractKit.Pack("other", "1.0.0"))).StatusCode.Should().Be(HttpStatusCode.OK);

    var response = await host.InstallAsync(TakeOverContractKit.Pack("pns-daily"), takeOver: takeOver);

    response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    var body = await GamepackContractHost.ReadAsync(response);
    body.GetProperty("valid").GetBoolean().Should().BeFalse();
    body.GetProperty("problems")[0].GetProperty("category").GetString().Should().Be("conflict");
    body.GetProperty("problems")[0].GetProperty("owner").GetString().Should().StartWith("other");
  }

  [Fact]
  public async Task AUserObjectThatIsNotInThePackStaysUnchanged() {
    using var host = new GamepackContractHost();
    await TakeOverContractKit.MakeUserObjectsAsync(host);
    (await host.Client.PostAsJsonAsync(new Uri("/api/sequences", UriKind.Relative), new { name = "Unrelated" })).StatusCode.Should().Be(HttpStatusCode.Created);
    var unrelatedId = await host.SequenceIdAsync("Unrelated");

    (await host.InstallAsync(TakeOverContractKit.Pack(), takeOver: true)).StatusCode.Should().Be(HttpStatusCode.OK);

    (await TakeOverContractKit.OwnerKindOfSequenceAsync(host, unrelatedId)).Should().Be("user");
  }

  [Fact]
  public async Task AnInstallWithNoConflictAndTheOptionOnHasNoBackup() {
    using var host = new GamepackContractHost();

    var response = await host.InstallAsync(TakeOverContractKit.Pack(), takeOver: true);

    response.StatusCode.Should().Be(HttpStatusCode.OK);
    var body = await GamepackContractHost.ReadAsync(response);
    body.GetProperty("takenOver").GetInt32().Should().Be(0);
    body.TryGetProperty("takeOverBackupId", out _).Should().BeFalse();
    TakeOverContractKit.BackupFileCount(host).Should().Be(0);
  }

  [Fact]
  public async Task AFailedWriteGives500WithNoBackupIdAndLeavesTheUserObjects() {
    using var host = new GamepackContractHost(withThrowingGate: true);
    await TakeOverContractKit.MakeUserObjectsAsync(host);
    var sequenceId = await host.SequenceIdAsync("Help All");
    host.Gate.Arm(3);

    var response = await host.InstallAsync(TakeOverContractKit.Pack(), takeOver: true);

    response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
    var body = await GamepackContractHost.ReadAsync(response);
    body.TryGetProperty("takeOverBackupId", out _).Should().BeFalse();
    (await TakeOverContractKit.OwnerKindOfSequenceAsync(host, sequenceId)).Should().Be("user");
    TakeOverContractKit.BackupFileCount(host).Should().Be(0);
  }
}
