using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using GameBot.Domain.Gamepacks;
using Xunit;

#pragma warning disable CA1707, CA2007, CA1861, CA1416, CA2000, CA1849, CA1031

namespace GameBot.UnitTests.Gamepacks;

/// <summary>Issue #333 (T023): a write error for one object of each kind names the file, the kind and the id.</summary>
public sealed class GamepackApplierWriteErrorTests {
  internal static string V1() => WriteErrorFixtures.V1();

  internal static string V2() => WriteErrorFixtures.V2();

  internal static async Task<(GamepackTestKit Kit, FailOnceStores Stores, GamepackApplier Applier, GamepackPlan Plan)> ArrangeAsync() {
    var kit = new GamepackTestKit();
    (await kit.Service.InstallAsync(V1(), false)).Status.Should().Be(GamepackInstallStatus.Ok);
    var stores = new FailOnceStores(kit);
    var plan = (await kit.CheckAsync(V2())).Plan!;
    return (kit, stores, stores.NewApplier(kit), plan);
  }

  [Theory]
  [InlineData("sequence", "s1")]
  [InlineData("command", "c1")]
  [InlineData("image", "img")]
  public async Task AFailedWriteGivesTheFileErrorAndRestoresTheOldState(string kind, string id) {
    var (kit, stores, applier, plan) = await ArrangeAsync();
    using var _ = kit;
    var before = kit.Snapshot();
    switch (kind) {
      case "sequence": stores.Sequences.FailNext = true; break;
      case "command": stores.Commands.FailNext = true; break;
      default: stores.Images.FailNext = true; break;
    }

    var outcome = await applier.ApplyAsync(plan, GamepackPreviousHandling.Capture);

    var path = kind switch {
      "sequence" => kit.Sequences.GetFilePath(id),
      "command" => kit.Commands.GetFilePath(id),
      _ => kit.Images.GetFilePath(id)
    };
    outcome.Status.Should().Be(GamepackApplyStatus.Failed);
    outcome.Error!.Message.Should().Be($"Cannot replace file '{path}' for {kind} '{id}': boom");
    outcome.FailedRestoreKeys.Should().BeEmpty();
    kit.Snapshot().Should().BeEquivalentTo(before);
  }

  [Fact]
  public async Task AFailedTemplateWriteGivesTheFileError() {
    using var kit = new GamepackTestKit();
    var stores = new FailOnceStores(kit);
    var applier = stores.NewApplier(kit);
    var json = GamepackApplierSeedTests.Pack(GamepackTestKit.Seed("Daily", GamepackTestKit.SeedEntry("Help All")));
    var parsed = GamepackFileSerializer.Parse(json);
    var plan = (await kit.Validator.ValidateAsync(parsed.File!, parsed.Problems, seeds: true)).Plan!;
    var before = kit.Snapshot();
    stores.Templates.FailNext = true;

    var outcome = await applier.ApplyAsync(plan, GamepackPreviousHandling.Keep);

    outcome.Status.Should().Be(GamepackApplyStatus.Failed);
    outcome.Error!.Message.Should().MatchRegex("^Cannot replace file '.+\\.json' for template '[^']+': boom$");
    kit.Snapshot().Should().BeEquivalentTo(before);
  }
}
