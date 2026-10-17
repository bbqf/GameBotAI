using System;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using GameBot.Domain.Gamepacks;
using GameBot.UnitTests.Gamepacks;
using Xunit;

#pragma warning disable CA2007, CA1861, CA1707, CA1416

namespace GameBot.IntegrationTests.Gamepacks;

/// <summary>Feature 152: the dry run and the install of a gamepack with a parameter placeholder image id (issue 326).</summary>
public sealed class GamepackDryRunPlaceholderImageTests : IDisposable {
  private readonly GamepackTestKit _kit = new();

  public void Dispose() => _kit.Dispose();

  [Fact]
  public async Task DryRunAcceptsADeclaredPlaceholderImageId() {
    var outcome = await _kit.InstallTakeOverAsync(GamepackPlaceholderImageFixtures.Issue326Pack(), dryRun: true);

    outcome.Status.Should().Be(GamepackInstallStatus.Ok);
    outcome.Problems.Should().BeEmpty();
  }

  [Fact]
  public async Task InstallAcceptsADeclaredPlaceholderImageId() {
    var outcome = await _kit.InstallTakeOverAsync(GamepackPlaceholderImageFixtures.Issue326Pack());

    outcome.Status.Should().Be(GamepackInstallStatus.Ok);
    (await _kit.SequenceByNameAsync(GamepackPlaceholderImageFixtures.SequenceName)).Should().NotBeNull();
  }

  [Fact]
  public async Task DryRunStillRejectsAnUndeclaredPlaceholderName() {
    var outcome = await _kit.InstallTakeOverAsync(GamepackPlaceholderImageFixtures.Issue326Pack("otherName"), dryRun: true);

    outcome.Status.Should().Be(GamepackInstallStatus.Invalid);
    outcome.Problems.Should().ContainSingle(p => p.Category == "reference" && p.Message.Contains("{{novaOptionImage}}"));
  }
}
