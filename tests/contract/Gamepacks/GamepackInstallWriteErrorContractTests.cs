using System.Net;
using System.Threading.Tasks;
using FluentAssertions;
using GameBot.Domain.Commands;
using GameBot.UnitTests.Gamepacks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

#pragma warning disable CA2007, CA1861, CA1707, CA2000, CA1305, CA1307, CA1310, CA1849

namespace GameBot.ContractTests.Gamepacks;

/// <summary>Issue #333 (T026, T027): a write error gives 500 with the file error text, and the state stays as before.</summary>
public sealed class GamepackInstallWriteErrorContractTests {
  private static GamepackContractHost NewHost() => new(configure: (services, root) => {
    services.RemoveAll<ISequenceRepository>();
    services.AddSingleton<ISequenceRepository>(new FailOnceSequences(new FileSequenceRepository(root)));
  });

  [Fact]
  public async Task AFailedFileWriteGives500WithTheFileErrorText() {
    using var host = NewHost();
    (await host.InstallAsync(WriteErrorFixtures.V1())).StatusCode.Should().Be(HttpStatusCode.OK);
    var sequences = (FailOnceSequences)host.Services.GetRequiredService<ISequenceRepository>();
    sequences.FailNext = true;

    var response = await host.InstallAsync(WriteErrorFixtures.V2());

    response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
    var body = await GamepackContractHost.ReadAsync(response);
    body.GetProperty("result").GetString().Should().Be("failed");
    body.GetProperty("detail").GetString().Should().Be($"Cannot replace file '{sequences.GetFilePath("s1")}' for sequence 's1': boom");
    body.TryGetProperty("failedRestoreKeys", out _).Should().BeFalse("only partial_restore has the list");
  }

  [Fact]
  public async Task AFailedFileWriteWithAWorkingRestoreLeavesEveryObjectAsBefore() {
    using var host = NewHost();
    (await host.InstallAsync(WriteErrorFixtures.V1())).StatusCode.Should().Be(HttpStatusCode.OK);
    var before = await host.StateSnapshotAsync();
    ((FailOnceSequences)host.Services.GetRequiredService<ISequenceRepository>()).FailNext = true;

    var response = await host.InstallAsync(WriteErrorFixtures.V2());

    response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
    (await GamepackContractHost.ReadAsync(response)).GetProperty("result").GetString().Should().Be("failed");
    (await host.StateSnapshotAsync()).Should().Be(before);
    (await host.GetJsonAsync("/api/gamepacks"))[0].GetProperty("version").GetString().Should().Be("1");
  }
}
