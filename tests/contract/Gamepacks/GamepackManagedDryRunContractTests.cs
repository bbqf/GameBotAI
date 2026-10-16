using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using FluentAssertions;
using GameBot.UnitTests.Gamepacks;
using Xunit;

#pragma warning disable CA2007, CA1861, CA1707, CA2000, CA1305, CA1307, CA1310, CA1849

namespace GameBot.ContractTests.Gamepacks;

/// <summary>Feature 148 (T042, T043): the dry run on the update routes.</summary>
public sealed class GamepackManagedDryRunContractTests {
  private static async Task<ManagedContractHost> ArrangeAsync() {
    var host = new ManagedContractHost();
    (await host.Host.InstallAsync(ManagedContractHost.Pack("1.0.0", "alpha", "beta"))).EnsureSuccessStatusCode();
    await host.AddTemplateAndQueueAsync("daily", running: true, "alpha", "beta");
    return host;
  }

  [Fact]
  public async Task ADryRunGivesAPlannedReportAndChangesNothing() {
    using var host = await ArrangeAsync();
    var before = await host.Host.StateSnapshotAsync();

    var response = await host.UpdateAsync(ManagedContractHost.Pack("2.0.0", "alpha", "gamma"), dryRun: true);

    response.StatusCode.Should().Be(HttpStatusCode.OK);
    var body = await GamepackContractHost.ReadAsync(response);
    body.GetProperty("result").GetString().Should().Be("planned");
    body.GetProperty("dryRun").GetBoolean().Should().BeTrue();
    body.GetProperty("stoppedQueues").GetArrayLength().Should().Be(1);
    body.GetProperty("newEntries").GetArrayLength().Should().Be(1);
    host.Control.Calls.Should().BeEmpty();
    (await host.Host.StateSnapshotAsync()).Should().Be(before);
  }

  [Fact]
  public async Task AMissingTemplateGives400ForADryRunToo() {
    using var host = await ArrangeAsync();

    var response = await host.UpdateAsync(ManagedContractHost.Pack("2.0.0", "alpha"), dryRun: true, templates: new[] { "nope" });

    response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    var body = await GamepackContractHost.ReadAsync(response);
    body.GetProperty("reason").GetString().Should().Be("template_not_found");
    body.GetProperty("result").GetString().Should().Be("rejected");
  }

  [Fact]
  public async Task AFileSignatureOrValidationProblemGives200ForADryRunAnd422ForANormalCall() {
    using var host = await ArrangeAsync();
    await host.Host.SetPolicyAsync("required");
    var pack = ManagedContractHost.Pack("2.0.0", "alpha");

    var dry = await host.UpdateAsync(pack, dryRun: true);
    var normal = await host.UpdateAsync(pack);
    var badFile = await host.UpdateAsync("not json", dryRun: true);

    dry.StatusCode.Should().Be(HttpStatusCode.OK);
    var body = await GamepackContractHost.ReadAsync(dry);
    body.GetProperty("result").GetString().Should().Be("rejected");
    body.GetProperty("reason").GetString().Should().Be("signature_rejected");
    body.GetProperty("signature").GetProperty("wouldReject").GetBoolean().Should().BeTrue();
    normal.StatusCode.Should().Be((HttpStatusCode)422);
    badFile.StatusCode.Should().Be(HttpStatusCode.OK);
    (await GamepackContractHost.ReadAsync(badFile)).GetProperty("reason").GetString().Should().Be("invalid_file");
  }

  [Fact]
  public async Task AMissingTemplateAndASignatureProblemAreBothInTheReport() {
    using var host = await ArrangeAsync();
    await host.Host.SetPolicyAsync("required");

    var response = await host.UpdateAsync(ManagedContractHost.Pack("2.0.0", "alpha"), dryRun: true, templates: new[] { "nope" });

    response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    var body = await GamepackContractHost.ReadAsync(response);
    body.GetProperty("reason").GetString().Should().Be("template_not_found");
    var codes = body.GetProperty("problems").EnumerateArray().Select(p => p.TryGetProperty("code", out var c) ? c.GetString() : null).ToList();
    codes.Should().Contain("template_not_found");
    codes.Should().Contain(c => c != null && c.StartsWith("signature_", StringComparison.Ordinal));
  }

  [Fact]
  public async Task AMalformedRequestGivesAClientErrorForADryRun() {
    using var host = await ArrangeAsync();
    var content = new MultipartFormDataContent { { new StringContent("x"), "other" } };

    var response = await host.Client.PostAsync(new Uri("/api/gamepacks/update?dryRun=true", UriKind.Relative), content);
    var noVersion = await host.CatalogUpdateAsync("pns", null, dryRun: true);

    response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    noVersion.StatusCode.Should().Be(HttpStatusCode.BadRequest);
  }

  [Fact]
  public async Task ADryRunWhileACallRunsGivesARejectedReportWith200() {
    using var host = await ArrangeAsync();
    var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    host.Control.OnStop = async _ => {
      entered.TrySetResult();
      await release.Task;
    };
    var first = host.UpdateAsync(ManagedContractHost.Pack("2.0.0", "alpha", "gamma"));
    await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));

    var dry = await host.UpdateAsync(ManagedContractHost.Pack("3.0.0", "alpha"), dryRun: true);

    dry.StatusCode.Should().Be(HttpStatusCode.OK);
    var body = await GamepackContractHost.ReadAsync(dry);
    body.GetProperty("result").GetString().Should().Be("rejected");
    body.GetProperty("reason").GetString().Should().Be("update_in_progress");
    release.SetResult();
    await first;
  }
}
