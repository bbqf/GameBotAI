using System.Collections.Generic;
using FluentAssertions;
using GameBot.Domain.Commands;
using GameBot.Domain.Gamepacks;
using Xunit;

namespace GameBot.UnitTests.Gamepacks;

/// <summary>Feature 192 (FR-018): the command id rewrite keeps the config and the placeholders of a TextInput step.</summary>
public sealed class GamepackReferencesTextInputTests {
  [Fact]
  public void TheRewriteKeepsTheTextInputStep() {
    var textStep = new CommandStep {
      Type = CommandStepType.TextInput,
      Order = 0,
      TextInput = new TextInputConfig { Text = "{{inline}}" },
      FieldTemplates = new Dictionary<string, string> { ["textInput.text"] = "{{fighterBatch}}" }
    };
    var command = new Command { Id = "c", Name = "C" };
    command.Steps.Add(textStep);
    command.Steps.Add(new CommandStep { Type = CommandStepType.Command, Order = 1, TargetId = "old-id" });

    GamepackReferences.RewriteCommandIds(command, id => id == "old-id" ? "new-id" : null);

    command.Steps[0].TextInput!.Text.Should().Be("{{inline}}");
    command.Steps[0].FieldTemplates.Should().ContainKey("textInput.text").WhoseValue.Should().Be("{{fighterBatch}}");
    command.Steps[1].TargetId.Should().Be("new-id");
  }

  [Fact]
  public void TheRewriteOfACommandStepCopiesATextInputConfig() {
    var config = new TextInputConfig { Text = "250" };
    var command = new Command { Id = "c", Name = "C" };
    command.Steps.Add(new CommandStep { Type = CommandStepType.Command, Order = 0, TargetId = "old-id", TextInput = config });

    GamepackReferences.RewriteCommandIds(command, _ => "new-id");

    command.Steps[0].TextInput.Should().BeSameAs(config);
  }
}
