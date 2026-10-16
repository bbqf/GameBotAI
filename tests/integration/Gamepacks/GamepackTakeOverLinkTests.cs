using System;
using System.Threading.Tasks;
using FluentAssertions;
using GameBot.Domain.Gamepacks;
using GameBot.Domain.QueueTemplates;
using GameBot.UnitTests.Gamepacks;
using Xunit;

#pragma warning disable CA2007, CA1861, CA1707, CA1416

namespace GameBot.IntegrationTests.Gamepacks;

/// <summary>Feature 149 (T016): a link to a taken-over sequence stays valid, and the gamepack owns the sequence.</summary>
public sealed class GamepackTakeOverLinkTests : IDisposable {
  private readonly GamepackTestKit _kit = new();

  public void Dispose() => _kit.Dispose();

  [Fact]
  public async Task AQueueTemplateLinkStaysValidAfterTheTakeOverOfASequence() {
    await _kit.AddUserObjectsAsync(GamepackTestKit.Pack("user", "1",
      sequences: new[] { GamepackTestKit.Seq("u-daily", "Daily", "u-tap") },
      commands: new[] { GamepackTestKit.Cmd("u-tap", "Tap") }));
    var userSequenceId = (await _kit.SequenceByNameAsync("Daily"))!.Id;
    var templates = new FileQueueTemplateRepository(_kit.Root);
    var template = new QueueTemplate { Id = Guid.NewGuid().ToString("N"), Name = "daily-template" };
    template.Entries.Add(new QueueTemplateEntry { SequenceId = userSequenceId });
    await templates.CreateAsync(template);

    var outcome = await _kit.InstallTakeOverAsync(GamepackTestKit.Pack("pns", "1.0",
      sequences: new[] { GamepackTestKit.Seq("p-daily", "Daily", "p-tap") },
      commands: new[] { GamepackTestKit.Cmd("p-tap", "Tap", imageId: "icon") },
      images: new[] { GamepackTestKit.Img("icon") }));

    outcome.Status.Should().Be(GamepackInstallStatus.Ok);
    outcome.TakenOver.Should().Be(2);
    var stored = (await templates.FindByNameAsync("daily-template"))!;
    var linked = await _kit.Sequences.GetAsync(stored.Entries[0].SequenceId);
    linked.Should().NotBeNull("the template link must stay valid");
    linked!.Name.Should().Be("Daily");
    _kit.OwnerOf(GamepackObjectTypes.Sequence, linked.Id).Name.Should().Be("pns");
    _kit.OwnerOf(GamepackObjectTypes.Sequence, linked.Id).Version.Should().Be("1.0");
  }
}
