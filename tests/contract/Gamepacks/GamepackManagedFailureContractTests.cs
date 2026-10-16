using System;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using FluentAssertions;
using Xunit;

#pragma warning disable CA2007, CA1861, CA1707, CA2000, CA1305, CA1307, CA1310, CA1849

namespace GameBot.ContractTests.Gamepacks;

/// <summary>Feature 148 (T038): a step that fails after the start gives a 500 report with the restore.</summary>
public sealed class GamepackManagedFailureContractTests {
  [Fact]
  public async Task AFailedInstallGives500WithAFailedReportAndARestore() {
    using var host = new ManagedContractHost(withThrowingGate: true);
    (await host.Host.InstallAsync(ManagedContractHost.Pack("1.0.0", "alpha"))).EnsureSuccessStatusCode();
    await host.AddTemplateAndQueueAsync("daily", running: true, "alpha");
    host.Host.Gate.Arm(1);

    var response = await host.UpdateAsync(ManagedContractHost.Pack("2.0.0", "alpha", "gamma"));

    response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
    var body = await GamepackContractHost.ReadAsync(response);
    body.GetProperty("result").GetString().Should().Be("failed");
    body.GetProperty("reason").GetString().Should().Be("install_failed");
    var restore = body.GetProperty("restore");
    restore.GetProperty("happened").GetBoolean().Should().BeTrue();
    restore.GetProperty("restored").GetBoolean().Should().BeTrue();
    restore.GetProperty("steps").GetArrayLength().Should().BeGreaterThan(0);
    host.Control.Running.Should().HaveCount(1);
    var list = await host.Host.GetJsonAsync("/api/gamepacks");
    list.EnumerateArray().Single().GetProperty("version").GetString().Should().Be("1.0.0");
  }

  [Fact]
  public async Task AnUnknownFailureAfterTheStartGivesTheReasonUnexpected() {
    using var host = new ManagedContractHost();
    (await host.Host.InstallAsync(ManagedContractHost.Pack("1.0.0", "alpha"))).EnsureSuccessStatusCode();
    await host.AddTemplateAndQueueAsync("daily", running: true, "alpha");
    host.Control.OnStop = _ => throw new InvalidOperationException("Injected stop fault.");

    var response = await host.UpdateAsync(ManagedContractHost.Pack("2.0.0", "alpha", "gamma"));

    response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
    var body = await GamepackContractHost.ReadAsync(response);
    body.GetProperty("result").GetString().Should().Be("failed");
    body.GetProperty("reason").GetString().Should().Be("unexpected");
    body.GetProperty("restore").GetProperty("happened").GetBoolean().Should().BeTrue();
  }
}
