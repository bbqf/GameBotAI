#pragma warning disable CA1861, CA1869 // Test data arrays and options are small and used one time.
using System.Linq;
using System.Text.Json;
using FluentAssertions;
using GameBot.Domain.Commands;
using GameBot.Domain.Parameters;
using Xunit;

namespace GameBot.UnitTests.Parameters;

/// <summary>Feature 168: the new declaration members survive save and load, and old data loads as before.</summary>
public sealed class ParameterDeclarationFriendlyFieldsTests {
  private static readonly JsonSerializerOptions WebOptions =
      new(JsonSerializerDefaults.Web) { WriteIndented = true };

  private static ParameterDeclaration Full() => new() {
    Name = "speed",
    Type = ParameterValueType.Number,
    Default = "5",
    Label = "Speed",
    HelpText = "How fast the bot runs.",
    Kind = ParameterInputKind.Number,
    Min = 1,
    Max = 9,
    Unit = "x",
    Pattern = "emulator-{value}"
  };

  [Fact]
  public void CommandRoundTripsAllNewMembers() {
    var command = new Command { Id = "c1", Name = "Run" };
    command.Parameters.Add(Full());
    command.Parameters.Add(new ParameterDeclaration {
      Name = "mode", Kind = ParameterInputKind.Choice,
      Options = new[] { new ParameterOption { Label = "Fast", Value = "f" } }
    });
    command.Parameters.Add(new ParameterDeclaration {
      Name = "img", Kind = ParameterInputKind.Image, AllowedImages = new[] { "a", "b" }
    });

    var round = JsonSerializer.Deserialize<Command>(JsonSerializer.Serialize(command, WebOptions), WebOptions)!;

    var speed = round.Parameters.Single(p => p.Name == "speed");
    speed.Label.Should().Be("Speed");
    speed.HelpText.Should().Be("How fast the bot runs.");
    speed.Kind.Should().Be(ParameterInputKind.Number);
    speed.Min.Should().Be(1);
    speed.Max.Should().Be(9);
    speed.Unit.Should().Be("x");
    speed.Pattern.Should().Be("emulator-{value}");

    var mode = round.Parameters.Single(p => p.Name == "mode");
    mode.Options.Should().ContainSingle(o => o.Label == "Fast" && o.Value == "f");

    round.Parameters.Single(p => p.Name == "img").AllowedImages.Should().Equal("a", "b");
  }

  [Fact]
  public void SequenceRoundTripsAllNewMembers() {
    var sequence = new CommandSequence { Id = "s1", Name = "Daily" };
    sequence.Parameters.Add(Full());

    var round = JsonSerializer.Deserialize<CommandSequence>(JsonSerializer.Serialize(sequence), new JsonSerializerOptions())!;

    round.Parameters.Single().Pattern.Should().Be("emulator-{value}");
    round.Parameters.Single().Kind.Should().Be(ParameterInputKind.Number);
  }

  [Fact]
  public void DeclarationWithoutNewMembersSerializesWithoutThem() {
    var json = JsonSerializer.Serialize(
        new ParameterDeclaration { Name = "a", Default = "x" }, WebOptions);

    foreach (var member in new[] { "label", "helpText", "kind", "options", "allowedImages", "min", "max", "unit", "pattern" }) {
      json.Should().NotContain($"\"{member}\"");
    }
  }

  [Fact]
  public void PreFeatureDeclarationLoadsWithEmptyNewMembers() {
    const string json = """
      { "name": "a", "type": 1, "default": "3", "required": true, "description": "d" }
      """;

    var declaration = JsonSerializer.Deserialize<ParameterDeclaration>(json, WebOptions)!;

    declaration.Type.Should().Be(ParameterValueType.Number);
    declaration.Kind.Should().BeNull();
    declaration.Options.Should().BeNull();
    declaration.EffectiveKind.Should().Be(ParameterInputKind.Number);
  }

  [Fact]
  public void EffectiveKindFollowsTheTypeWhenKindIsEmpty() {
    new ParameterDeclaration { Name = "t" }.EffectiveKind.Should().Be(ParameterInputKind.Text);
    new ParameterDeclaration { Name = "n", Type = ParameterValueType.Number }.EffectiveKind
        .Should().Be(ParameterInputKind.Number);
    new ParameterDeclaration { Name = "c", Kind = ParameterInputKind.Choice }.EffectiveKind
        .Should().Be(ParameterInputKind.Choice);
  }
}
