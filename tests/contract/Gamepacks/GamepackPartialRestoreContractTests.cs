using System;
using System.Linq;
using System.Net;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using GameBot.UnitTests.Gamepacks;
using Xunit;

#pragma warning disable CA2007, CA1861, CA1707, CA2000, CA1305, CA1307, CA1310, CA1849

namespace GameBot.ContractTests.Gamepacks;

/// <summary>Issue #333 (T032, FR-005, FR-009): a partial_restore response lists the files in an unknown state.</summary>
public sealed class GamepackPartialRestoreContractTests {
  private const string KeyPattern = "^(sequence|command|image|template|registry|previous):[^ ]+ \\(.+\\)$";

  private static string[] Keys(JsonElement body) =>
    body.GetProperty("failedRestoreKeys").EnumerateArray().Select(k => k.GetString()!).ToArray();

  private static void AssertPartial(JsonElement body) {
    body.GetProperty("result").GetString().Should().Be("partial_restore");
    var keys = Keys(body);
    keys.Should().NotBeEmpty();
    keys.Should().OnlyContain(k => System.Text.RegularExpressions.Regex.IsMatch(k, KeyPattern), "each entry is kind:id (full path)");
  }

  [Fact]
  public async Task TheInstallRouteSendsTheKeyList() {
    using var host = new GamepackContractHost(withThrowingGate: true);
    host.Gate.Arm(4);
    host.Gate.ThrowOnRestore = true;

    var response = await host.InstallAsync(WriteErrorFixtures.V1());

    response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
    var body = await GamepackContractHost.ReadAsync(response);
    AssertPartial(body);
    Keys(body).Should().Contain(k => k.StartsWith("registry:", StringComparison.Ordinal));
  }

  [Fact]
  public async Task TheRollbackRouteSendsTheKeyList() {
    using var host = new GamepackContractHost(withThrowingGate: true);
    (await host.InstallAsync(WriteErrorFixtures.V1())).StatusCode.Should().Be(HttpStatusCode.OK);
    (await host.InstallAsync(WriteErrorFixtures.V2())).StatusCode.Should().Be(HttpStatusCode.OK);
    host.Gate.Arm(2);
    host.Gate.ThrowOnRestore = true;

    var response = await host.RollbackAsync("p");

    response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
    AssertPartial(await GamepackContractHost.ReadAsync(response));
  }

  [Fact]
  public async Task TheTakeOverRestoreRouteSendsTheKeyList() {
    using var host = new GamepackContractHost(withThrowingGate: true);
    await TakeOverContractKit.MakeUserObjectsAsync(host);
    var install = await host.InstallAsync(TakeOverContractKit.Pack(), takeOver: true);
    install.StatusCode.Should().Be(HttpStatusCode.OK);
    var backupId = (await GamepackContractHost.ReadAsync(install)).GetProperty("takeOverBackupId").GetString()!;
    host.Gate.Arm(2);
    host.Gate.ThrowOnRestore = true;

    var response = await host.RestoreTakeOverAsync(backupId);

    response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
    AssertPartial(await GamepackContractHost.ReadAsync(response));
  }
}
