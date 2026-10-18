using System.IO;
using System.Threading.Tasks;
using FluentAssertions;
using GameBot.Domain.Gamepacks;
using Xunit;

#pragma warning disable CA1707, CA2007, CA1861, CA1416, CA2000, CA1849, CA2000, CA1031

namespace GameBot.UnitTests.Gamepacks;

/// <summary>Issue #333 (T024, FR-008): an image error that has the file text already gets no second prefix.</summary>
public sealed class GamepackApplierImagePrefixTests {
  [Fact]
  public async Task AnImageErrorHasExactlyOnePrefix() {
    var (kit, stores, applier, plan) = await GamepackApplierWriteErrorTests.ArrangeAsync();
    using var _ = kit;
    var path = kit.Images.GetFilePath("img");
    stores.Message = $"Cannot replace file '{path}' for image 'img': locked";
    stores.Images.FailNext = true;

    var outcome = await applier.ApplyAsync(plan, GamepackPreviousHandling.Keep);

    outcome.Status.Should().Be(GamepackApplyStatus.Failed);
    outcome.Error!.Message.Should().Be($"Cannot replace file '{path}' for image 'img': locked");
    outcome.Error.Should().BeOfType<IOException>();
  }

  [Fact]
  public async Task ARealLockedImageGivesOnePrefixAndTheRestoreFailsToo() {
    if (!System.OperatingSystem.IsWindows()) {
      return;
    }

    var (kit, _, _, plan) = await GamepackApplierWriteErrorTests.ArrangeAsync();
    using var _ = kit;
    var path = kit.Images.GetFilePath("img")!;
    using var hold = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);

    var outcome = await kit.Applier.ApplyAsync(plan, GamepackPreviousHandling.Keep);

    outcome.Status.Should().Be(GamepackApplyStatus.PartialRestore);
    outcome.Error!.Message.Should().StartWith($"Cannot replace file '{path}' for image 'img': ");
    outcome.Error.Message.Split("Cannot replace file").Length.Should().Be(2, "there is one prefix only");
  }
}
