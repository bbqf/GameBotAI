using System;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using GameBot.Domain.Gamepacks;
using Xunit;

#pragma warning disable CA1707, CA2007, CA1861, CA1416, CA2000, CA1849, CA2000, CA1031

namespace GameBot.UnitTests.Gamepacks;

/// <summary>Issue #333 (T028, FR-005, FR-009): a failed restore lists every file in an unknown state as kind:id (full path).</summary>
public sealed class GamepackApplierRestoreFailureTests {
  private static string V1() => GamepackTestKit.Pack("p", "1",
    sequences: new[] { GamepackTestKit.Seq("s1", "Seq", "c1"), GamepackTestKit.Seq("s2", "Old Seq") },
    commands: new[] { GamepackTestKit.Cmd("c1", "Cmd", imageId: "img") },
    images: new[] { GamepackTestKit.Img("img") });

  private static string V2() => GamepackTestKit.Pack("p", "2",
    sequences: new[] { GamepackTestKit.Seq("s1", "Seq", "c1"), GamepackTestKit.Seq("s3", "New Seq") },
    commands: new[] { GamepackTestKit.Cmd("c1", "Cmd", imageId: "img2") },
    images: new[] { GamepackTestKit.Img("img2") });

  [Fact]
  public async Task AWriteAndRestoreFaultListsEveryKindWithItsPath() {
    int total;
    using (var count = new GamepackTestKit()) {
      (await count.Service.InstallAsync(V1(), false)).Status.Should().Be(GamepackInstallStatus.Ok);
      count.Gate.Arm(0);
      (await count.Service.InstallAsync(V2(), false)).Status.Should().Be(GamepackInstallStatus.Ok);
      total = count.Gate.Calls;
    }

    using var kit = new GamepackTestKit();
    (await kit.Service.InstallAsync(V1(), false)).Status.Should().Be(GamepackInstallStatus.Ok);
    kit.Gate.Arm(total);
    kit.Gate.ThrowOnRestore = true;

    var outcome = await kit.Service.InstallAsync(V2(), false);

    outcome.Status.Should().Be(GamepackInstallStatus.PartialRestore);
    outcome.FailedRestoreKeys.Should().Contain(new[] {
      $"registry:p ({kit.Store.RegistryFilePath})",
      $"previous:p ({kit.Store.PreviousFilePath("p")})",
      $"sequence:s1 ({kit.Sequences.GetFilePath("s1")})",
      $"sequence:s2 ({kit.Sequences.GetFilePath("s2")})",
      $"sequence:s3 ({kit.Sequences.GetFilePath("s3")})",
      $"command:c1 ({kit.Commands.GetFilePath("c1")})",
      $"image:img ({kit.Images.GetFilePath("img")})",
      $"image:img2 ({kit.Images.GetFilePath("img2")})"
    });
  }

  [Fact]
  public async Task ARealLockedFileWithAFailedRestoreIsListedOnce() {
    if (!OperatingSystem.IsWindows()) {
      return;
    }

    using var kit = new GamepackTestKit();
    (await kit.Service.InstallAsync(V1(), false)).Status.Should().Be(GamepackInstallStatus.Ok);
    var path = kit.Sequences.GetFilePath("s1")!;
    using var hold = new System.IO.FileStream(path, System.IO.FileMode.Open, System.IO.FileAccess.Read, System.IO.FileShare.Read);

    var outcome = await kit.Service.InstallAsync(V2(), false);

    outcome.Status.Should().Be(GamepackInstallStatus.PartialRestore);
    outcome.ErrorMessage.Should().StartWith($"Cannot replace file '{path}' for sequence 's1': ");
    outcome.FailedRestoreKeys.Should().ContainSingle().Which.Should().Be($"sequence:s1 ({path})");
  }
}
