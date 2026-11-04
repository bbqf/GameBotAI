using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http.Json;
using System.Threading.Tasks;
using FluentAssertions;
using GameBot.Domain.Commands;
using GameBot.UnitTests.Gamepacks;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using W = GameBot.UnitTests.Gamepacks.WaitForImagePackFixtures;

#pragma warning disable CA2007, CA1861, CA1707, CA2000, CA1305, CA1307, CA1310, CA1849

namespace GameBot.ContractTests.Gamepacks;

/// <summary>Feature 177 (B-039): an installed inline WaitForImage step keeps its configuration.</summary>
public sealed class GamepackWaitForImageInstallContractTests {
  internal static async Task<CommandSequence> StoredAsync(GamepackContractHost host, string name = W.SequenceName) {
    var repository = host.Services.GetRequiredService<ISequenceRepository>();
    return (await repository.ListAsync()).Single(s => s.Name == name);
  }

  internal static SequenceStep Find(IEnumerable<SequenceStep> steps, string stepId) {
    foreach (var step in steps) {
      if (step.StepId == stepId) return step;
      var inner = Find(step.Body, stepId) ?? (step.ElseBody is null ? null : Find(step.ElseBody, stepId));
      if (inner is not null) return inner;
    }

    return null!;
  }

  private static void ExpectConfig(SequenceStep step, int timeoutMs) {
    step.WaitForImage.Should().NotBeNull($"step '{step.StepId}' must keep its configuration");
    step.WaitForImage!.TimeoutMs.Should().Be(timeoutMs);
    step.WaitForImage.DetectionTarget.Should().NotBeNull();
    step.WaitForImage.DetectionTarget!.ReferenceImageId.Should().Be(W.ImageId);
  }

  [Fact]
  public async Task Install_InlineWaitForImage_StoresConfiguration() {
    using var host = new GamepackContractHost();

    var response = await host.InstallAsync(W.MainPack());

    response.StatusCode.Should().Be(HttpStatusCode.OK);
    var stored = await StoredAsync(host);
    var top = Find(stored.Steps, "top");
    ExpectConfig(top, 3000);
    top.WaitForImage!.DetectionTarget!.Confidence.Should().Be(0.85);
  }

  [Fact]
  public async Task Install_NestedWaitForImage_StoresConfigurationAtEveryDepth() {
    using var host = new GamepackContractHost();

    (await host.InstallAsync(W.MainPack())).StatusCode.Should().Be(HttpStatusCode.OK);

    var steps = (await StoredAsync(host)).Steps;
    ExpectConfig(Find(steps, "inLoop"), 1100);
    ExpectConfig(Find(steps, "inIf"), 1200);
    ExpectConfig(Find(steps, "inElse"), 1300);
    ExpectConfig(Find(steps, "deep"), 1400);
  }

  [Fact]
  public async Task Install_AbsentAndRegionOptions_AreStored() {
    using var host = new GamepackContractHost();

    (await host.InstallAsync(W.MainPack())).StatusCode.Should().Be(HttpStatusCode.OK);

    var steps = (await StoredAsync(host)).Steps;
    var absent = Find(steps, "absent").WaitForImage!;
    absent.Until.Should().Be("absent");
    absent.AbsentCaptures.Should().Be(3);
    absent.DetectionTarget!.SelectionStrategy.Should().Be(DetectionSelectionStrategy.FirstMatch);
    absent.DetectionTarget.OffsetX.Should().Be(4);
    var region = Find(steps, "region").WaitForImage!.DetectionTarget!.Region;
    region.Should().NotBeNull();
    region!.Width.Should().Be(300);
  }

  [Fact]
  public async Task Install_OtherStepType_IsUnchanged() {
    using var host = new GamepackContractHost();

    (await host.InstallAsync(W.MainPack())).StatusCode.Should().Be(HttpStatusCode.OK);

    var other = Find((await StoredAsync(host)).Steps, "other");
    other.WaitForImage.Should().BeNull();
    other.Action!.Type.Should().Be("WaitForStill");
  }

  [Fact]
  public async Task TakeOverInstall_StoresConfiguration() {
    using var host = new GamepackContractHost();
    (await host.Client.PostAsJsonAsync(new Uri("/api/sequences", UriKind.Relative), new { name = W.SequenceName })).StatusCode.Should().Be(HttpStatusCode.Created);

    var response = await host.InstallAsync(W.MainPack(), takeOver: true);

    response.StatusCode.Should().Be(HttpStatusCode.OK);
    var stored = await StoredAsync(host);
    ExpectConfig(Find(stored.Steps, "top"), 3000);
    // The stored content is the content that the plan hashed: a second take-over dry run finds no user object to take over.
    var again = await GamepackContractHost.ReadAsync(await host.InstallAsync(W.MainPack("1.0.0"), dryRun: true, takeOver: true));
    again.GetProperty("valid").GetBoolean().Should().BeTrue();
  }

  [Fact]
  public async Task Update_WritesTheConfigurationOfAnOldFormSequence() {
    using var host = new GamepackContractHost();
    (await host.InstallAsync(W.MainPack("1.0.0"))).StatusCode.Should().Be(HttpStatusCode.OK);
    // Make the stored sequence look as the bug stored it: the action payload only.
    var repository = host.Services.GetRequiredService<ISequenceRepository>();
    var old = await StoredAsync(host);
    foreach (var step in Flatten(old.Steps)) step.WaitForImage = null;
    await repository.UpdateAsync(old);
    Find((await StoredAsync(host)).Steps, "top").WaitForImage.Should().BeNull();

    var response = await host.InstallAsync(W.MainPack("1.1.0"));

    response.StatusCode.Should().Be(HttpStatusCode.OK);
    var steps = (await StoredAsync(host)).Steps;
    ExpectConfig(Find(steps, "top"), 3000);
    ExpectConfig(Find(steps, "deep"), 1400);
  }

  [Fact]
  public async Task Install_MissingReferenceImage_GivesTheSameResultAsBefore() {
    using var host = new GamepackContractHost();

    var response = await host.InstallAsync(W.MainPack());

    // Image-reference checks run on the steps as read from the file. They do not see the new configuration.
    response.StatusCode.Should().Be(HttpStatusCode.OK);
    var body = await GamepackContractHost.ReadAsync(response);
    body.GetProperty("valid").GetBoolean().Should().BeTrue();
    body.GetProperty("problems").GetArrayLength().Should().Be(0);
  }

  private static IEnumerable<SequenceStep> Flatten(IEnumerable<SequenceStep> steps) {
    foreach (var step in steps) {
      yield return step;
      foreach (var child in Flatten(step.Body)) yield return child;
      if (step.ElseBody is not null) foreach (var child in Flatten(step.ElseBody)) yield return child;
    }
  }
}
