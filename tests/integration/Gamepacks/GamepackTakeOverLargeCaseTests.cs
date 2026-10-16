using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using GameBot.Domain.Gamepacks;
using GameBot.Domain.Gamepacks.Managed;
using GameBot.UnitTests.Gamepacks;
using GameBot.UnitTests.Gamepacks.Managed;
using Xunit;

#pragma warning disable CA2007, CA1861, CA1707, CA1416

namespace GameBot.IntegrationTests.Gamepacks;

/// <summary>Feature 149 (T019, SC-001, SC-008): the large case of 13 sequences and 131 commands.</summary>
public sealed class GamepackTakeOverLargeCaseTests {
  internal const int SequenceCount = 13;
  internal const int CommandCount = 131;

  internal static string UserPack() => GamepackTestKit.Pack("user", "1",
    sequences: Enumerable.Range(0, SequenceCount).Select(i => GamepackTestKit.Seq("us" + i, "Seq " + i, "uc" + i)).ToArray(),
    commands: Enumerable.Range(0, CommandCount).Select(i => GamepackTestKit.Cmd("uc" + i, "Cmd " + i)).ToArray());

  internal static string BigPack(string version = "1.0") => GamepackTestKit.Pack("big", version,
    sequences: Enumerable.Range(0, SequenceCount).Select(i => GamepackTestKit.Seq("ps" + i, "Seq " + i, "pc" + i, "pc" + (i + SequenceCount))).ToArray(),
    commands: Enumerable.Range(0, CommandCount).Select(i => GamepackTestKit.Cmd("pc" + i, "Cmd " + i, imageId: "icon")).ToArray(),
    images: new[] { GamepackTestKit.Img("icon") });

  [Fact]
  public async Task ATakeOverOfAllObjectsKeepsEveryLinkValidAndChangesNoQueueState() {
    using var rig = new ManagedTestRig();
    await rig.Packs.AddUserObjectsAsync(UserPack());
    var template = await rig.AddTemplateAsync("all", Enumerable.Range(0, SequenceCount).Select(i => "Seq " + i).ToArray());
    var queue = await rig.AddQueueAsync("Q", template, running: true);

    var report = await rig.RunAsync(new ManagedUpdateRequest { Mode = ManagedUpdateMode.Update, FileText = BigPack(), TakeOver = true });

    report.Result.Should().Be(ManagedUpdateResults.Done, ManagedTestRig.Describe(report));
    report.TakenOver.Should().Be(SequenceCount + CommandCount);
    report.Created.Should().Be(1);
    rig.Control.Calls.Should().BeEmpty("a first install with the option on stops and starts no queue");
    rig.Control.Running.Should().Contain(queue.Id);

    var commandIds = (await rig.Packs.Commands.ListAsync()).Select(c => c.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
    commandIds.Should().HaveCount(CommandCount);
    foreach (var sequence in await rig.Packs.Sequences.ListAsync()) {
      GamepackReferences.CommandIds(sequence).Should().OnlyContain(id => commandIds.Contains(id), "the sequence " + sequence.Name);
    }

    var stored = await rig.TemplateAsync("all");
    foreach (var entry in stored.Entries) {
      (await rig.Packs.Sequences.GetAsync(entry.SequenceId)).Should().NotBeNull();
    }

    rig.Packs.Store.Load().Owners.Values.Should().OnlyContain(o => o.Gamepack == "big");
  }
}
