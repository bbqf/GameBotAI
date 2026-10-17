using System;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Xunit;
using F = GameBot.UnitTests.Gamepacks.GamepackStepFixtures;

#pragma warning disable CA1707, CA1861, CA1305, CA1307, CA1310, CA2007

namespace GameBot.UnitTests.Gamepacks;

/// <summary>Feature 151: a step problem blocks the install, and a missing command gives a reference problem.</summary>
public sealed class GamepackValidatorStepTests : IDisposable {
  private readonly GamepackTestKit _kit = new();

  public void Dispose() => _kit.Dispose();

  [Fact]
  public async Task AStepProblemGivesAStructureProblemAndNoPlan() {
    var result = await _kit.CheckAsync(F.PackWith(F.UnknownMember()));

    result.IsValid.Should().BeFalse();
    result.Plan.Should().BeNull();
    result.Problems.Should().Contain(p => p.Category == "structure" && p.Message.Contains("'mystery'"));
  }

  [Fact]
  public async Task AStepProblemDoesNotCreateTheSequenceOnInstall() {
    var outcome = await _kit.Service.InstallAsync(F.PackWith(F.UnknownMember()), new GameBot.Domain.Gamepacks.GamepackInstallOptions(false));

    outcome.Should().NotBeNull();
    (await _kit.SequenceByNameAsync(F.SequenceName)).Should().BeNull();
  }

  [Theory]
  [InlineData("UnresolvablePrimitive")]
  [InlineData("UnresolvableReference")]
  [InlineData("UnresolvableCommandId")]
  public async Task AStepThatNamesAMissingCommandGivesAReferenceProblem(string form) {
    var step = typeof(F).GetMethod(form)!.Invoke(null, null)!.ToString()!;

    var result = await _kit.CheckAsync(F.PackWith(step));

    result.IsValid.Should().BeFalse();
    result.Problems.Should().ContainSingle(p => p.Category == "reference" && p.Message.Contains("missing-cmd"));
  }

  [Fact]
  public async Task TheIssue321FileIsValid() {
    var result = await _kit.CheckAsync(F.Issue321Pack());

    result.Problems.Should().BeEmpty();
    result.IsValid.Should().BeTrue();
  }
}
