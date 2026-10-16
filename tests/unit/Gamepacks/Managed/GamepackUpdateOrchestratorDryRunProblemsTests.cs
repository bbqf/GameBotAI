using System;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using GameBot.Domain.Gamepacks.Managed;
using Xunit;

#pragma warning disable CA2007, CA1707, CA1861, CA1869

namespace GameBot.UnitTests.Gamepacks.Managed;

/// <summary>Feature 148 (T043): a dry run reports all problems together in one report.</summary>
public sealed class GamepackUpdateOrchestratorDryRunProblemsTests {
  [Fact]
  public async Task AValidationProblemAMissingTemplateAndARunningCallAreAllInOneReport() {
    using var rig = new ManagedTestRig();
    await rig.InstallAsync("1.0.0", "alpha");
    var template = await rig.AddTemplateAsync("daily", "alpha");
    await rig.AddQueueAsync("Q", template, running: true);
    // A command of the user with the name of a gamepack command is a conflict for the new file.
    await rig.Packs.Commands.AddAsync(MakeCommand("Taken"));
    var conflicting = GameBot.UnitTests.Gamepacks.GamepackTestKit.Pack(
      "other", "1.0.0",
      commands: new[] { GameBot.UnitTests.Gamepacks.GamepackTestKit.Cmd("c9", "Taken") });

    var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    rig.Control.OnStop = async _ => {
      entered.TrySetResult();
      await release.Task;
    };
    var running = rig.RunAsync(ManagedTestRig.Update(ManagedTestRig.Pack("2.0.0", "alpha", "gamma")));
    await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));

    var report = await rig.RunAsync(ManagedTestRig.Update(conflicting, true, null, "nope"));

    report.Result.Should().Be(ManagedUpdateResults.Rejected);
    report.Reason.Should().Be(ManagedUpdateReasons.TemplateNotFound);
    report.Problems.Select(p => p.Category).Should().Contain("conflict");
    report.Problems.Should().Contain(p => p.Code == ManagedUpdateReasons.TemplateNotFound);
    report.Problems.Should().Contain(p => p.Code == ManagedUpdateReasons.UpdateInProgress);

    release.SetResult();
    await running;
  }

  [Fact]
  public async Task ASignatureProblemGivesRejectedForADryRunAndForANormalCall() {
    using var rig = new ManagedTestRig();
    rig.SignatureSettings.SetPolicy(GameBot.Domain.Gamepacks.Signing.SignaturePolicy.Required);

    var dry = await rig.RunAsync(ManagedTestRig.Update(ManagedTestRig.Pack("1.0.0", "alpha"), true));
    var normal = await rig.RunAsync(ManagedTestRig.Update(ManagedTestRig.Pack("1.0.0", "alpha")));

    dry.Reason.Should().Be(ManagedUpdateReasons.SignatureRejected);
    dry.Signature!.WouldReject.Should().BeTrue();
    normal.Reason.Should().Be(ManagedUpdateReasons.SignatureRejected);
    normal.Result.Should().Be(ManagedUpdateResults.Rejected);
    rig.Service.List().Should().BeEmpty();
    rig.Control.Calls.Should().BeEmpty();
  }

  private static GameBot.Domain.Commands.Command MakeCommand(string name) {
    var json = "{\"id\":\"user-1\",\"name\":\"" + name + "\",\"steps\":[{\"type\":\"KeyInput\",\"order\":0,\"keyInput\":{\"key\":\"BACK\"}}]}";
    var options = new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web);
    options.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter());
    return System.Text.Json.JsonSerializer.Deserialize<GameBot.Domain.Commands.Command>(json, options)!;
  }
}
