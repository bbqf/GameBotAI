using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Threading.Tasks;
using FluentAssertions;
using GameBot.Domain.Gamepacks.Signing;
using GameBot.UnitTests.Gamepacks;
using Xunit;

#pragma warning disable CA2007, CA1861, CA1707, CA2000, CA1305, CA1307, CA1310, CA1849

namespace GameBot.ContractTests.Gamepacks;

/// <summary>Feature 148 (T025): the update routes.</summary>
public sealed class GamepackManagedUpdateContractTests {
  private static readonly string[] ReportFields = {
    "result", "reason", "dryRun", "gamepack", "previousVersion", "stoppedQueues", "restartedQueues", "notRestartedQueues",
    "backupId", "created", "updated", "removed", "newEntries", "removedSequenceEntries", "notAffectedTemplates", "problems",
    "signature", "restore"
  };

  private static async Task<ManagedContractHost> ArrangeAsync(bool running = true) {
    var host = new ManagedContractHost();
    (await host.Host.InstallAsync(ManagedContractHost.Pack("1.0.0", "alpha", "beta"))).EnsureSuccessStatusCode();
    await host.AddTemplateAndQueueAsync("daily", running, "alpha", "beta");
    return host;
  }

  [Fact]
  public async Task AnUpdateGivesADoneReportWithEveryField() {
    using var host = await ArrangeAsync();

    var response = await host.UpdateAsync(ManagedContractHost.Pack("2.0.0", "alpha", "gamma"));

    response.StatusCode.Should().Be(HttpStatusCode.OK);
    var body = await GamepackContractHost.ReadAsync(response);
    foreach (var field in ReportFields) {
      body.TryGetProperty(field, out _).Should().BeTrue("the report has the field " + field);
    }

    body.GetProperty("result").GetString().Should().Be("done");
    body.GetProperty("gamepack").GetProperty("version").GetString().Should().Be("2.0.0");
    body.GetProperty("previousVersion").GetString().Should().Be("1.0.0");
    body.GetProperty("stoppedQueues").GetArrayLength().Should().Be(1);
    body.GetProperty("restartedQueues").GetArrayLength().Should().Be(1);
    body.GetProperty("backupId").GetString().Should().NotBeNullOrEmpty();
    body.GetProperty("newEntries").EnumerateArray().Single().GetProperty("sequenceName").GetString().Should().Be("gamma");
    body.GetProperty("removedSequenceEntries").EnumerateArray().Single().GetProperty("sequenceName").GetString().Should().Be("beta");
    (await host.TemplateAsync("daily")).Entries.Last().Enabled.Should().BeFalse();
  }

  [Fact]
  public async Task AnEqualVersionGivesDoneWithNoChange() {
    using var host = await ArrangeAsync();

    var response = await host.UpdateAsync(ManagedContractHost.Pack("1.0.0", "alpha", "beta"));

    response.StatusCode.Should().Be(HttpStatusCode.OK);
    var body = await GamepackContractHost.ReadAsync(response);
    body.GetProperty("result").GetString().Should().Be("done");
    body.GetProperty("stoppedQueues").GetArrayLength().Should().Be(0);
    body.GetProperty("backupId").ValueKind.Should().Be(System.Text.Json.JsonValueKind.Null);
    host.Control.Calls.Should().BeEmpty();
  }

  [Fact]
  public async Task AMalformedRequestGives400WithOneReason() {
    using var host = await ArrangeAsync();
    var content = new MultipartFormDataContent { { new StringContent("x"), "other" } };

    var response = await host.Client.PostAsync(new Uri("/api/gamepacks/update", UriKind.Relative), content);

    response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    (await ManagedContractHost.ReasonOfAsync(response)).Should().Be("invalid_request");
  }

  [Fact]
  public async Task AFileThatIsNotJsonGives400InvalidFile() {
    using var host = await ArrangeAsync();

    var response = await host.UpdateAsync("not json");

    response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    (await ManagedContractHost.ReasonOfAsync(response)).Should().Be("invalid_file");
    host.Control.Calls.Should().BeEmpty();
  }

  [Fact]
  public async Task AMissingTemplateGives400WithARejectedReport() {
    using var host = await ArrangeAsync();

    var response = await host.UpdateAsync(ManagedContractHost.Pack("2.0.0", "alpha"), templates: new[] { "nope" });

    response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    var body = await GamepackContractHost.ReadAsync(response);
    body.GetProperty("result").GetString().Should().Be("rejected");
    body.GetProperty("reason").GetString().Should().Be("template_not_found");
    host.Control.Calls.Should().BeEmpty();
  }

  [Fact]
  public async Task ACheckProblemGives422ValidationFailed() {
    using var host = await ArrangeAsync();
    var bad = GamepackTestKit.Pack("pns", "2.0.0", sequences: new[] { GamepackTestKit.Seq("s1", "alpha", "missing-command") });

    var response = await host.UpdateAsync(bad);

    response.StatusCode.Should().Be((HttpStatusCode)422);
    var body = await GamepackContractHost.ReadAsync(response);
    body.GetProperty("reason").GetString().Should().Be("validation_failed");
    body.GetProperty("problems").GetArrayLength().Should().BeGreaterThan(0);
    host.Control.Calls.Should().BeEmpty();
  }

  [Fact]
  public async Task ARejectedSignatureGives422AndStopsNoQueue() {
    using var host = await ArrangeAsync();
    await host.Host.SetPolicyAsync("required");

    var response = await host.UpdateAsync(ManagedContractHost.Pack("2.0.0", "alpha"));

    response.StatusCode.Should().Be((HttpStatusCode)422);
    var body = await GamepackContractHost.ReadAsync(response);
    body.GetProperty("reason").GetString().Should().Be("signature_rejected");
    body.GetProperty("signature").GetProperty("wouldReject").GetBoolean().Should().BeTrue();
    host.Control.Calls.Should().BeEmpty();
  }

  [Fact]
  public async Task ASecondCallWhileOneRunsGives409UpdateInProgress() {
    using var host = await ArrangeAsync();
    var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    host.Control.OnStop = async _ => {
      entered.TrySetResult();
      await release.Task;
    };
    var first = host.UpdateAsync(ManagedContractHost.Pack("2.0.0", "alpha", "gamma"));
    await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));

    var second = await host.UpdateAsync(ManagedContractHost.Pack("3.0.0", "alpha"));

    second.StatusCode.Should().Be(HttpStatusCode.Conflict);
    (await ManagedContractHost.ReasonOfAsync(second)).Should().Be("update_in_progress");
    release.SetResult();
    (await first).StatusCode.Should().Be(HttpStatusCode.OK);
  }

  [Fact]
  public async Task ARequestOverTheLimitGives413() {
    using var host = await ArrangeAsync();

    var response = await host.Client.PostAsync(new Uri("/api/gamepacks/update", UriKind.Relative), new FakeLengthContent(100L * 1024 * 1024 + 1));

    response.StatusCode.Should().Be(HttpStatusCode.RequestEntityTooLarge);
    (await ManagedContractHost.ReasonOfAsync(response)).Should().Be("too_large");
  }

  [Theory]
  [InlineData("/api/gamepacks/update")]
  [InlineData("/api/gamepacks/catalog/update")]
  [InlineData("/api/gamepacks/pns/rollback?managed=true")]
  public async Task ACallWithNoTokenIsRefused(string path) {
    using var host = new ManagedContractHost();
    using var anonymous = host.Host.CreateAnonymousClient();

    var response = await anonymous.PostAsync(new Uri(path, UriKind.Relative), new StringContent("{}", Encoding.UTF8, "application/json"));

    response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
  }

  // ---- the catalog route ----

  [Fact]
  public async Task TheCatalogRouteUpdatesFromAFolderCatalog() {
    using var host = await ArrangeAsync();
    var folder = Path.Combine(host.Host.Root, "catalog");
    new CatalogFixture().Add("pns", "2.0.0", ManagedContractHost.Pack("2.0.0", "alpha", "gamma")).WriteTo(folder);
    await host.Host.SetCatalogAsync(folder);

    var response = await host.CatalogUpdateAsync("pns", "2.0.0");

    response.StatusCode.Should().Be(HttpStatusCode.OK);
    var body = await GamepackContractHost.ReadAsync(response);
    body.GetProperty("result").GetString().Should().Be("done");
    body.GetProperty("gamepack").GetProperty("version").GetString().Should().Be("2.0.0");
  }

  [Fact]
  public async Task TheCatalogRouteMapsTheCatalogFailuresLikeFeature147() {
    using var host = await ArrangeAsync();

    var notConfigured = await host.CatalogUpdateAsync("pns", "2.0.0");
    var folder = Path.Combine(host.Host.Root, "catalog");
    new CatalogFixture().Add("other", "1.0.0", ManagedContractHost.Pack("1.0.0", "x")).WriteTo(folder);
    await host.Host.SetCatalogAsync(folder);
    var notListed = await host.CatalogUpdateAsync("pns", "9.9.9");
    var malformed = await host.CatalogUpdateAsync(null, "1.0.0");

    notConfigured.StatusCode.Should().Be(HttpStatusCode.Conflict);
    (await ManagedContractHost.ReasonOfAsync(notConfigured)).Should().Be("not_configured");
    notListed.StatusCode.Should().Be(HttpStatusCode.NotFound);
    (await ManagedContractHost.ReasonOfAsync(notListed)).Should().Be("entry_not_listed");
    malformed.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    (await ManagedContractHost.ReasonOfAsync(malformed)).Should().Be("invalid_request");
    host.Control.Calls.Should().BeEmpty();
  }

  [Fact]
  public async Task TheCatalogProtocolOfFeature147DoesNotChange() {
    using var host = new ManagedContractHost();
    var folder = Path.Combine(host.Host.Root, "catalog");
    new CatalogFixture().Add("pns", "1.0.0", ManagedContractHost.Pack("1.0.0", "alpha"), "First.").WriteTo(folder);
    await host.Host.SetCatalogAsync(folder);

    var list = await host.Host.CatalogListAsync();
    var install = await host.Host.CatalogInstallAsync("pns", "1.0.0");

    list.StatusCode.Should().Be(HttpStatusCode.OK);
    (await GamepackContractHost.ReadAsync(list)).GetProperty("gamepacks").GetArrayLength().Should().Be(1);
    install.StatusCode.Should().Be(HttpStatusCode.OK);
    var body = await GamepackContractHost.ReadAsync(install);
    body.GetProperty("valid").GetBoolean().Should().BeTrue();
    body.TryGetProperty("reason", out _).Should().BeFalse();
  }
}
