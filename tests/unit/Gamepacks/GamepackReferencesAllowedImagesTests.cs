#pragma warning disable CA1861 // Test data arrays are small and used one time.
using FluentAssertions;
using GameBot.Domain.Commands;
using GameBot.Domain.Gamepacks;
using GameBot.Domain.Parameters;
using Xunit;

namespace GameBot.UnitTests.Gamepacks;

/// <summary>Feature 168 (T028): an allowed image of an image parameter is an image reference of the pack.</summary>
public sealed class GamepackReferencesAllowedImagesTests {
  private static ParameterDeclaration Pic() => new() {
    Name = "pic", Kind = ParameterInputKind.Image, AllowedImages = new[] { "img-a", "img-b" }
  };

  [Fact]
  public void SequenceImageIdsIncludeAllowedImages() {
    var sequence = new CommandSequence { Id = "s1", Name = "S" };
    sequence.Parameters.Add(Pic());

    GamepackReferences.ImageIds(sequence).Should().BeEquivalentTo(new[] { "img-a", "img-b" });
  }

  [Fact]
  public void CommandImageIdsIncludeAllowedImages() {
    var command = new Command { Id = "c1", Name = "C" };
    command.Parameters.Add(Pic());

    GamepackReferences.ImageIds(command).Should().BeEquivalentTo(new[] { "img-a", "img-b" });
  }

  [Fact]
  public void DeclarationWithoutAllowedImagesAddsNothing() {
    var command = new Command { Id = "c1", Name = "C" };
    command.Parameters.Add(new ParameterDeclaration { Name = "t" });

    GamepackReferences.ImageIds(command).Should().BeEmpty();
  }
}
