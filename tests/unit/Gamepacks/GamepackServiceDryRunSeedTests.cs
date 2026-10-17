using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using GameBot.Domain.Gamepacks;
using Xunit;

#pragma warning disable CA1707, CA2007, CA1861, CA2000

namespace GameBot.UnitTests.Gamepacks;

/// <summary>Feature 150 (T030): a dry run with seeds makes no template write.</summary>
public sealed class GamepackServiceDryRunSeedTests {
  [Fact]
  public async Task ADryRunWithSeedsMakesNoTemplateWrite() {
    using var kit = new GamepackTestKit();
    await kit.AddTemplateAsync("Used");
    var recording = new RecordingTemplateRepository(kit.Templates);
    var validator = new GamepackValidator(kit.Sequences, kit.Commands, kit.Images, kit.Store, recording);
    var applier = new GamepackApplier(kit.Sequences, kit.Commands, kit.Images, kit.Store, kit.Gate, templates: recording);
    using var service = new GamepackService(kit.Sequences, kit.Commands, kit.Images, kit.Store, validator, applier);
    var json = GamepackApplierSeedTests.Pack(
      GamepackTestKit.Seed("Daily", GamepackTestKit.SeedEntry("Help All")),
      GamepackTestKit.Seed("Used", GamepackTestKit.SeedEntry("Help All")));

    var outcome = await service.InstallAsync(json, dryRun: true);

    outcome.Status.Should().Be(GamepackInstallStatus.Ok);
    outcome.SeedTemplatesCreated.Should().Equal("Daily");
    outcome.SeedTemplatesSkipped.Select(s => s.Name).Should().Equal("Used");
    recording.Creates.Should().Be(0);
    recording.Deletes.Should().Be(0);
    (await kit.Templates.ListAsync()).Select(t => t.Name).Should().Equal("Used");
    kit.Store.Load().Gamepacks.Should().BeEmpty();
  }
}
