using System.Net;
using System.Text.Json;
using FluentAssertions;
using GameBot.Domain.Gamepacks;
using GameBot.UnitTests.Gamepacks;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

#pragma warning disable CA2007, CA1861, CA1707, CA2000

namespace GameBot.ContractTests.Gamepacks;

/// <summary>Feature 144: the install, the dry run, and the rollback with requiredApiMajor.</summary>
public sealed class GamepackApiVersionContractTests {
  private static string PackWithMajor(string version, int? major, string seqName = "Seq One") =>
    JsonSerializer.Serialize(new {
      manifest = new { name = "pns-daily", version, changelog = "", requiredApiMajor = major },
      sequences = new[] { GamepackTestKit.Seq("s1", seqName) },
      commands = Array.Empty<object>(),
      images = Array.Empty<object>()
    });

  private static int OtherMajor => ApiVersion.Major + 1;

  [Fact]
  public async Task AnInstallOfAnotherMajorGives422AndChangesNothing() {
    using var host = new GamepackContractHost();

    var response = await host.InstallAsync(PackWithMajor("1.0.0", OtherMajor));

    response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    var body = await GamepackContractHost.ReadAsync(response);
    var problem = body.GetProperty("problems")[0];
    problem.GetProperty("category").GetString().Should().Be("structure");
    problem.GetProperty("objectType").GetString().Should().Be("manifest");
    problem.GetProperty("message").GetString().Should()
      .Be($"The gamepack needs API major version {OtherMajor}. This platform runs API major version {ApiVersion.Major}.");
    (await host.GetJsonAsync("/api/gamepacks")).GetArrayLength().Should().Be(0);
    (await host.GetJsonAsync("/api/sequences")).GetArrayLength().Should().Be(0);
  }

  [Fact]
  public async Task ADryRunOfAnotherMajorGives200WithValidFalse() {
    using var host = new GamepackContractHost();

    var response = await host.InstallAsync(PackWithMajor("1.0.0", OtherMajor), dryRun: true);

    response.StatusCode.Should().Be(HttpStatusCode.OK);
    var body = await GamepackContractHost.ReadAsync(response);
    body.GetProperty("valid").GetBoolean().Should().BeFalse();
    body.GetProperty("problems")[0].GetProperty("message").GetString().Should().Contain("API major version " + OtherMajor);
  }

  [Fact]
  public async Task TheRunningMajorAndNoFieldInstallAsBefore() {
    using var host = new GamepackContractHost();

    (await host.InstallAsync(PackWithMajor("1.0.0", ApiVersion.Major))).StatusCode.Should().Be(HttpStatusCode.OK);
    (await host.InstallAsync(PackWithMajor("1.1.0", null))).StatusCode.Should().Be(HttpStatusCode.OK);
  }

  [Fact]
  public async Task ARollbackToAFileOfAnotherMajorWorks() {
    using var host = new GamepackContractHost();
    (await host.InstallAsync(PackWithMajor("1.0.0", ApiVersion.Major, "Seq One"))).StatusCode.Should().Be(HttpStatusCode.OK);
    (await host.InstallAsync(PackWithMajor("1.1.0", ApiVersion.Major, "Seq Two"))).StatusCode.Should().Be(HttpStatusCode.OK);

    // The install refuses a file of another major, so the test writes the kept previous file to the store directly.
    var store = host.Services.GetRequiredService<IGamepackRegistryStore>();
    store.WritePrevious("pns-daily", PackWithMajor("1.0.0", OtherMajor, "Seq One"));

    var response = await host.RollbackAsync("pns-daily");

    response.StatusCode.Should().Be(HttpStatusCode.OK);
    (await host.GetJsonAsync("/api/gamepacks"))[0].GetProperty("version").GetString().Should().Be("1.0.0");
  }

  [Fact]
  public async Task TheSwaggerTextForRequiredApiMajorStatesTheRefusal() {
    using var host = new GamepackContractHost();
    var response = await host.Client.GetAsync(new Uri("/swagger/v1/swagger.json", UriKind.Relative));
    var text = await response.Content.ReadAsStringAsync();

    text.Should().Contain("refuses a gamepack when requiredApiMajor differs from the running API major version");
  }
}
