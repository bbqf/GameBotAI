using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using GameBot.Domain.Commands;
using GameBot.Domain.Gamepacks;
using Xunit;

#pragma warning disable CA1707, CA2007, CA1861, CA1416

namespace GameBot.UnitTests.Gamepacks;

/// <summary>Feature 178 (T029): the validator lists only the update writes whose stored content differs from the file.</summary>
public sealed class GamepackOverwriteCompareTests : IDisposable {
  private readonly GamepackTestKit _kit = new();

  public void Dispose() => _kit.Dispose();

  internal static string Pack(string version = "1") => GamepackTestKit.Pack("pns", version,
    sequences: new[] { GamepackTestKit.Seq("s1", "Daily", "c1"), GamepackTestKit.Seq("s2", "Other") },
    commands: new[] { GamepackTestKit.Cmd("c1", "Tap", imageId: "icon") },
    images: new[] { GamepackTestKit.Img("icon") });

  private async Task InstallAsync(string json) {
    var outcome = await _kit.Service.InstallAsync(json, dryRun: false);
    outcome.Status.Should().Be(GamepackInstallStatus.Ok, string.Join("; ", outcome.Problems.Select(p => p.Message)) + outcome.ErrorMessage);
  }

  private async Task<GamepackPlan> PlanAsync(string json, bool reinstall, bool takeOver = false) {
    var parsed = GamepackFileSerializer.Parse(json);
    var result = await _kit.Validator.ValidateAsync(parsed.File!, parsed.Problems, default, takeOver: takeOver, reinstall: reinstall);
    result.IsValid.Should().BeTrue(string.Join("; ", result.Problems.Select(p => p.Message)));
    return result.Plan!;
  }

  [Fact]
  public async Task AnEqualInstalledPackHasNoOverwrites() {
    await InstallAsync(Pack());

    var plan = await PlanAsync(Pack(), reinstall: true);

    plan.Updated.Should().Be(4);
    plan.Overwrites.Should().BeEmpty();
  }

  [Fact]
  public async Task WithoutTheFlagTheListIsEmptyAndNoLiveContentIsSet() {
    await InstallAsync(Pack());
    var command = (await _kit.CommandByNameAsync("Tap"))!;
    command.Steps.Clear();
    await _kit.Commands.UpdateAsync(command);

    var plan = await PlanAsync(Pack(), reinstall: false);

    plan.Overwrites.Should().BeEmpty();
    plan.Writes.Should().OnlyContain(w => w.LiveCommand == null && w.LiveSequence == null);
  }

  [Fact]
  public async Task AHandChangedCommandIsListed() {
    await InstallAsync(Pack());
    var command = (await _kit.CommandByNameAsync("Tap"))!;
    command.Steps.Clear();
    command.Steps.Add(new CommandStep { Type = CommandStepType.KeyInput, KeyInput = new KeyInputConfig { Key = "HAND" } });
    await _kit.Commands.UpdateAsync(command);

    var plan = await PlanAsync(Pack(), reinstall: true);

    plan.Overwrites.Should().Equal(new OverwriteEntry(GamepackObjectTypes.Command, "Tap"));
  }

  [Fact]
  public async Task AHandChangedSequenceIsListed() {
    await InstallAsync(Pack());
    var sequence = (await _kit.SequenceByNameAsync("Other"))!;
    sequence.WatchdogTimeoutMs = 12345;
    await _kit.Sequences.UpdateAsync(sequence);

    var plan = await PlanAsync(Pack(), reinstall: true);

    plan.Overwrites.Select(o => (o.Type, o.Name)).Should().Contain((GamepackObjectTypes.Sequence, "Other"));
    plan.Overwrites.Should().NotContain(o => o.Name == "Daily");
  }

  [Fact]
  public async Task AHandChangedImageIsListed() {
    await InstallAsync(Pack());
    using var stream = new MemoryStream(new byte[] { 1, 2, 3, 4 });
    await _kit.Images.SaveAsync("icon", stream, "image/png", "icon.png", overwrite: true);

    var plan = await PlanAsync(Pack(), reinstall: true);

    plan.Overwrites.Should().Equal(new OverwriteEntry(GamepackObjectTypes.Image, "icon"));
  }

  [Fact]
  public async Task AHandDeletedObjectIsCreatedAgainAndNotListed() {
    await InstallAsync(Pack());
    var other = (await _kit.SequenceByNameAsync("Other"))!;
    await _kit.Sequences.DeleteAsync(other.Id);

    var plan = await PlanAsync(Pack(), reinstall: true);

    plan.Created.Should().Be(1);
    plan.Overwrites.Should().BeEmpty();
  }

  [Fact]
  public async Task ATakeOverObjectIsNotListed() {
    await InstallAsync(Pack());
    await _kit.AddUserObjectsAsync(GamepackTestKit.Pack("user", "1", sequences: new[] { GamepackTestKit.Seq("u1", "User Seq") }));
    var withExtra = GamepackTestKit.Pack("pns", "1",
      sequences: new[] { GamepackTestKit.Seq("s1", "Daily", "c1"), GamepackTestKit.Seq("s2", "Other"), GamepackTestKit.Seq("sx", "User Seq") },
      commands: new[] { GamepackTestKit.Cmd("c1", "Tap", imageId: "icon") },
      images: new[] { GamepackTestKit.Img("icon") });

    var plan = await PlanAsync(withExtra, reinstall: true, takeOver: true);

    plan.TakenOver.Should().Be(1);
    plan.Overwrites.Should().BeEmpty();
  }
}
