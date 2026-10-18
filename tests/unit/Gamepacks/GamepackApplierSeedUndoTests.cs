using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using GameBot.Domain.Gamepacks;
using Xunit;

#pragma warning disable CA1707, CA2007, CA1861, CA2000

namespace GameBot.UnitTests.Gamepacks;

/// <summary>Feature 150 (T025, FR-014, FR-007): the best-effort undo of the seed templates.</summary>
public sealed class GamepackApplierSeedUndoTests {
  private static string Pack() => GamepackApplierSeedTests.Pack(
    GamepackTestKit.Seed("Daily", GamepackTestKit.SeedEntry("Help All")),
    GamepackTestKit.Seed("Weekly", GamepackTestKit.SeedEntry("Help All")));

  private static async Task<(GamepackTestKit Kit, RecordingTemplateRepository Templates, GamepackApplier Applier, GamepackPlan Plan)> ArrangeAsync() {
    var kit = new GamepackTestKit();
    var recording = new RecordingTemplateRepository(kit.Templates);
    var applier = new GamepackApplier(kit.Sequences, kit.Commands, kit.Images, kit.Store, kit.Gate, takeOverBackups: kit.TakeOverBackups, templates: recording);
    var parsed = GamepackFileSerializer.Parse(Pack());
    var plan = (await kit.Validator.ValidateAsync(parsed.File!, parsed.Problems, seeds: true)).Plan!;
    return (kit, recording, applier, plan);
  }

  [Fact]
  public async Task AFailureAfterTheSeedsDeletesTheCreatedTemplatesById() {
    var (kit, templates, applier, plan) = await ArrangeAsync();
    using var _ = kit;
    var before = kit.Snapshot();
    // The writes are: one sequence, two seed templates, then the previous file is not written (new install), then the registry.
    kit.Gate.Arm(4);

    var outcome = await applier.ApplyAsync(plan, GamepackPreviousHandling.Keep);

    outcome.Status.Should().Be(GamepackApplyStatus.Failed);
    templates.Creates.Should().Be(2);
    templates.Deletes.Should().Be(2);
    (await kit.Templates.ListAsync()).Should().BeEmpty();
    kit.Snapshot().Where(p => !p.Key.StartsWith("queue-templates", StringComparison.OrdinalIgnoreCase)).Should().BeEquivalentTo(before.Where(p => !p.Key.StartsWith("queue-templates", StringComparison.OrdinalIgnoreCase)));
  }

  [Fact]
  public async Task AFailedDeleteIsLoggedAndTheOtherTemplatesAreStillRemoved() {
    var (kit, templates, applier, plan) = await ArrangeAsync();
    using var _ = kit;
    templates.FailDeleteOfName.Add("Weekly");
    kit.Gate.Arm(4);

    var outcome = await applier.ApplyAsync(plan, GamepackPreviousHandling.Keep);

    outcome.Status.Should().Be(GamepackApplyStatus.PartialRestore);
    templates.DeleteAttempts.Should().HaveCount(2, "the undo continues after the first failure");
    (await kit.Templates.ListAsync()).Select(t => t.Name).Should().Equal("Weekly");
    (await kit.Sequences.ListAsync()).Should().BeEmpty("the object restore still ran");
    outcome.FailedRestoreKeys.Should().ContainSingle().Which.Should().StartWith("template:").And.EndWith(".json)");
  }

  [Fact]
  public async Task ARollbackDeletesNoSeedTemplate() {
    using var kit = new GamepackTestKit();
    (await kit.Service.InstallAsync(GamepackTestKit.Pack("p", "1", sequences: new[] { GamepackTestKit.Seq("a1", "Help All") }), false)).Status.Should().Be(GamepackInstallStatus.Ok);
    (await kit.Service.InstallAsync(GamepackTestKit.Pack("p", "2", sequences: new[] { GamepackTestKit.Seq("a1", "Help All") }), false)).Status.Should().Be(GamepackInstallStatus.Ok);
    await kit.AddTemplateAsync("Daily");

    var outcome = await kit.Service.RollbackAsync("p");

    outcome.Status.Should().Be(GamepackRollbackStatus.RolledBack);
    (await kit.Templates.ListAsync()).Select(t => t.Name).Should().Equal("Daily");
  }
}
