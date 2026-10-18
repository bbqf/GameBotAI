using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using GameBot.Domain.Gamepacks;
using Xunit;

#pragma warning disable CA1707, CA2007, CA1861, CA1416

namespace GameBot.UnitTests.Gamepacks;

/// <summary>Feature 143 (T012): fault injection at every write (SC-001, FR-003) and in the restore step (FR-021).</summary>
public sealed class GamepackApplierFaultInjectionTests {
  private static string V1() => GamepackTestKit.Pack("p", "1",
    sequences: new[] { GamepackTestKit.Seq("s1", "Seq", "c1"), GamepackTestKit.Seq("s2", "Old Seq") },
    commands: new[] { GamepackTestKit.Cmd("c1", "Cmd", imageId: "img") },
    images: new[] { GamepackTestKit.Img("img") });

  // Version 2 updates Seq and Cmd, creates a new image and sequence, and drops "Old Seq" and the old image.
  private static string V2() => GamepackTestKit.Pack("p", "2",
    sequences: new[] { GamepackTestKit.Seq("s1", "Seq", "c1"), GamepackTestKit.Seq("s3", "New Seq") },
    commands: new[] { GamepackTestKit.Cmd("c1", "Cmd", imageId: "img2") },
    images: new[] { GamepackTestKit.Img("img2") });

  private static async Task<int> CountWriteCallsAsync(string? first, string second) {
    using var kit = new GamepackTestKit();
    if (first is not null) {
      (await kit.Service.InstallAsync(first, false)).Status.Should().Be(GamepackInstallStatus.Ok);
    }

    kit.Gate.Arm(0);
    (await kit.Service.InstallAsync(second, false)).Status.Should().Be(GamepackInstallStatus.Ok);
    return kit.Gate.Calls;
  }

  [Fact]
  public async Task AFaultAtEveryWriteOfAnUpdateLeavesTheStateAsBefore() {
    var total = await CountWriteCallsAsync(V1(), V2());
    total.Should().BeGreaterThan(5);

    for (var n = 1; n <= total; n++) {
      using var kit = new GamepackTestKit();
      (await kit.Service.InstallAsync(V1(), false)).Status.Should().Be(GamepackInstallStatus.Ok);
      var before = kit.Snapshot();

      kit.Gate.Arm(n);
      var outcome = await kit.Service.InstallAsync(V2(), false);

      outcome.Status.Should().Be(GamepackInstallStatus.Failed, "a fault at write call " + n);
      kit.Snapshot().Should().BeEquivalentTo(before, "the state must equal the state before the request after a fault at call " + n);
    }
  }

  [Fact]
  public async Task AFaultAtEveryWriteOfAFirstInstallLeavesNothing() {
    var total = await CountWriteCallsAsync(null, V1());
    for (var n = 1; n <= total; n++) {
      using var kit = new GamepackTestKit();
      var before = kit.Snapshot();

      kit.Gate.Arm(n);
      var outcome = await kit.Service.InstallAsync(V1(), false);

      outcome.Status.Should().Be(GamepackInstallStatus.Failed, "a fault at write call " + n);
      kit.Snapshot().Should().BeEquivalentTo(before, "a fault at call " + n);
      (await kit.Sequences.ListAsync()).Should().BeEmpty();
      kit.Store.Load().Gamepacks.Should().BeEmpty();
    }
  }

  [Fact]
  public async Task AFaultInTheRestoreGivesPartialRestoreAndALoggedObject() {
    using var kit = new GamepackTestKit();
    (await kit.Service.InstallAsync(V1(), false)).Status.Should().Be(GamepackInstallStatus.Ok);

    var total = await CountWriteCallsAsync(V1(), V2());
    kit.Gate.Arm(total); // The registry write is the last write.
    kit.Gate.ThrowOnRestore = true;
    var outcome = await kit.Service.InstallAsync(V2(), false);

    outcome.Status.Should().Be(GamepackInstallStatus.PartialRestore);
    outcome.ErrorMessage.Should().NotBeNullOrEmpty();
    kit.Gate.Log.Count(l => l.StartsWith("restore:", StringComparison.Ordinal)).Should().BeGreaterThan(1,
      "the restore goes on with the other objects after one failure");
  }

  [Fact]
  public async Task TheApplierReportsTheFailedRestoreKeys() {
    using var kit = new GamepackTestKit();
    var parsed = GamepackFileSerializer.Parse(V1());
    var check = await kit.Validator.ValidateAsync(parsed.File!, parsed.Problems);
    check.IsValid.Should().BeTrue();

    var total = check.Plan!.Writes.Count + check.Plan.Removals.Count + 1;
    kit.Gate.Arm(total);
    kit.Gate.ThrowOnRestore = true;
    var outcome = await kit.Applier.ApplyAsync(check.Plan, GamepackPreviousHandling.Keep);

    outcome.Status.Should().Be(GamepackApplyStatus.PartialRestore);
    outcome.FailedRestoreKeys.Should().Contain("registry:p (" + kit.Store.RegistryPath + ")");
    outcome.FailedRestoreKeys.Count.Should().BeGreaterThan(2);
  }

  [Fact]
  public async Task AFaultBeforeTheFirstWriteChangesNothing() {
    using var kit = new GamepackTestKit();
    var before = kit.Snapshot();
    kit.Gate.Arm(1);
    var outcome = await kit.Service.InstallAsync(V1(), false);
    outcome.Status.Should().Be(GamepackInstallStatus.Failed);
    kit.Snapshot().Should().BeEquivalentTo(before);
  }

  [Fact]
  public async Task TheGateSeesEveryWriteKind() {
    using var kit = new GamepackTestKit();
    (await kit.Service.InstallAsync(V1(), false)).Status.Should().Be(GamepackInstallStatus.Ok);
    kit.Gate.Arm(0);
    (await kit.Service.InstallAsync(V2(), false)).Status.Should().Be(GamepackInstallStatus.Ok);

    var operations = new HashSet<string>(kit.Gate.Log.Select(l => l[..l.IndexOf(':', StringComparison.Ordinal)]));
    operations.Should().Contain(new[] {
      GamepackWriteOperations.Create, GamepackWriteOperations.Update, GamepackWriteOperations.Delete,
      GamepackWriteOperations.PreviousFile, GamepackWriteOperations.Registry });
  }
}
