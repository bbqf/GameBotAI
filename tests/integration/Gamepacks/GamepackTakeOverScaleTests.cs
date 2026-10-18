using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using GameBot.Domain.Gamepacks;
using GameBot.UnitTests.Gamepacks;
using Xunit;

#pragma warning disable CA2007, CA1861, CA1707, CA1416, CA2000, CA1849, CA2000, CA1031

namespace GameBot.IntegrationTests.Gamepacks;

/// <summary>
/// Issue #333 (T017, T018, FR-011, FR-012, SC-001): an install with take-over over the same number of objects as the PNS 1.0.0 pack
/// (12 sequences, 130 commands, 117 images, 259 in all). It uses real files and a synthetic pack.
/// </summary>
public sealed class GamepackTakeOverScaleTests {
  private const int Sequences = 12;
  private const int Commands = 130;
  private const int Images = 117;

  // A valid PNG with one extra byte at the end. The tag makes the content differ between the user object and the pack object.
  private static string PngWithTag(byte tag) =>
    Convert.ToBase64String(Convert.FromBase64String(GamepackTestKit.PngBase64).Concat(new[] { tag }).ToArray());

  private static object[] ImagesOf(int count, byte tag) =>
    Enumerable.Range(0, count).Select(i => GamepackTestKit.Img("img" + i, PngWithTag(tag))).ToArray();

  private static string UserPack(int sequences, int commands, int images) => GamepackTestKit.Pack("user", "1",
    sequences: Enumerable.Range(0, sequences).Select(i => GamepackTestKit.Seq("us" + i, "Seq " + i, "uc" + i)).ToArray(),
    commands: Enumerable.Range(0, commands).Select(i => GamepackTestKit.Cmd("uc" + i, "Cmd " + i)).ToArray(),
    images: ImagesOf(images, 1));

  private static string BigPack() => GamepackTestKit.Pack("big", "1.0",
    sequences: Enumerable.Range(0, Sequences).Select(i => GamepackTestKit.Seq("ps" + i, "Seq " + i, "pc" + i)).ToArray(),
    commands: Enumerable.Range(0, Commands).Select(i => GamepackTestKit.Cmd("pc" + i, "Cmd " + i, imageId: "img" + (i % Images))).ToArray(),
    images: ImagesOf(Images, 2));

  private static async Task AssertPackContentAsync(GamepackTestKit kit) {
    (await kit.Sequences.ListAsync()).Should().HaveCount(Sequences);
    (await kit.Commands.ListAsync()).Should().HaveCount(Commands);
    (await kit.Images.ListIdsAsync()).Should().HaveCount(Images);

    var expected = Convert.FromBase64String(PngWithTag(2));
    for (var i = 0; i < Images; i++) {
      (await File.ReadAllBytesAsync(kit.Images.GetFilePath("img" + i)!)).Should().Equal(expected, "image " + i);
    }

    var commandIds = (await kit.Commands.ListAsync()).Select(c => c.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
    commandIds.Should().OnlyContain(id => id.StartsWith("uc", StringComparison.Ordinal) || id.StartsWith("pc", StringComparison.Ordinal));
    foreach (var sequence in await kit.Sequences.ListAsync()) {
      GamepackReferences.CommandIds(sequence).Should().OnlyContain(id => commandIds.Contains(id), "the sequence " + sequence.Name);
    }

    kit.Store.Load().Owners.Values.Should().OnlyContain(o => o.Gamepack == "big");
  }

  [Fact]
  public async Task ATakeOverOfAll259ObjectsSucceedsAndTheDryRunAgrees() {
    using var kit = new GamepackTestKit();
    await kit.AddUserObjectsAsync(UserPack(Sequences, Commands, Images));

    var dry = await kit.InstallTakeOverAsync(BigPack(), dryRun: true);
    dry.Status.Should().Be(GamepackInstallStatus.Ok);
    dry.TakenOver.Should().Be(Sequences + Commands + Images);

    var real = await kit.InstallTakeOverAsync(BigPack());

    real.Status.Should().Be(GamepackInstallStatus.Ok, real.ErrorMessage);
    real.TakenOver.Should().Be(dry.TakenOver);
    real.Created.Should().Be(dry.Created);
    real.Updated.Should().Be(dry.Updated);
    await AssertPackContentAsync(kit);
  }

  [Fact]
  public async Task AMixedInstallOfNewAndExistingObjectsSucceeds() {
    using var kit = new GamepackTestKit();
    await kit.AddUserObjectsAsync(UserPack(Sequences / 2, Commands / 2, Images / 2));

    var real = await kit.InstallTakeOverAsync(BigPack());

    real.Status.Should().Be(GamepackInstallStatus.Ok, real.ErrorMessage);
    real.TakenOver.Should().Be(Sequences / 2 + Commands / 2 + Images / 2);
    real.Created.Should().Be(Sequences + Commands + Images - real.TakenOver);
    (await kit.Sequences.ListAsync()).Should().HaveCount(Sequences);
    (await kit.Commands.ListAsync()).Should().HaveCount(Commands);
    (await kit.Images.ListIdsAsync()).Should().HaveCount(Images);
  }
}
