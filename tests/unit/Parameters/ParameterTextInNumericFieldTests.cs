#pragma warning disable CA1861 // Test data arrays are small and used one time.
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using GameBot.Domain.Commands;
using GameBot.Domain.Parameters;
using GameBot.Domain.Services;
using Xunit;

namespace GameBot.UnitTests.Parameters;

/// <summary>Feature 168 (FR-026): a text-typed friendly parameter in a numeric field is refused at the command save.</summary>
public sealed class ParameterTextInNumericFieldTests {
  private static Command CommandWith(ParameterDeclaration declaration, string path = "swipe.durationMs") {
    var command = new Command { Id = "c1", Name = "Swipe" };
    command.Parameters.Add(declaration);
    command.Steps.Add(new CommandStep {
      Type = CommandStepType.Swipe,
      Order = 0,
      Swipe = new SwipeConfig { StartX = 1, StartY = 2, EndX = 3, EndY = 4 },
      FieldTemplates = new Dictionary<string, string> { [path] = "{{" + declaration.Name + "}}" }
    });
    return command;
  }

  private static ParameterDeclaration TextChoice() => new() {
    Name = "speed", Kind = ParameterInputKind.Choice,
    Options = new[] { new ParameterOption { Label = "Fast", Value = "100" } }
  };

  private static ParameterDeclaration NumberWithPattern() => new() {
    Name = "speed", Type = ParameterValueType.Number, Pattern = "x{value}"
  };

  [Fact]
  public void TextChoiceInNumericFieldIsRefusedWithHint() {
    var result = ParameterValidationService.ValidateCommand(CommandWith(TextChoice()));

    var error = result.Errors.Should().ContainSingle(e => e.Code == ParameterValidationCodes.TextParameterInNumericField).Subject;
    error.Message.Should().Contain("swipe.durationMs").And.Contain("speed").And.Contain(". ");
    error.ParameterName.Should().Be("speed");
  }

  [Fact]
  public void NumberWithPatternInNumericFieldIsRefused() {
    var result = ParameterValidationService.ValidateCommand(CommandWith(NumberWithPattern()));

    result.Errors.Should().Contain(e => e.Code == ParameterValidationCodes.TextParameterInNumericField);
  }

  [Fact]
  public void PlainNumberAndNumberChoiceAreAccepted() {
    ParameterValidationService.ValidateCommand(CommandWith(new ParameterDeclaration {
      Name = "speed", Type = ParameterValueType.Number
    })).Errors.Should().BeEmpty();

    ParameterValidationService.ValidateCommand(CommandWith(new ParameterDeclaration {
      Name = "speed", Type = ParameterValueType.Number, Kind = ParameterInputKind.Choice,
      Options = new[] { new ParameterOption { Label = "Fast", Value = "100" } }
    })).Errors.Should().BeEmpty();
  }

  [Fact]
  public void PlainTextParameterWithoutFriendlyMembersKeepsItsOldBehavior() {
    ParameterValidationService.ValidateCommand(CommandWith(new ParameterDeclaration { Name = "speed" }))
        .Errors.Should().NotContain(e => e.Code == ParameterValidationCodes.TextParameterInNumericField);
  }

  [Fact]
  public void TextChoiceInATextFieldIsAccepted() {
    var command = new Command { Id = "c1", Name = "Key" };
    command.Parameters.Add(TextChoice());
    command.Steps.Add(new CommandStep {
      Type = CommandStepType.KeyInput, Order = 0,
      KeyInput = new KeyInputConfig { Key = "{{speed}}" }
    });

    ParameterValidationService.ValidateCommand(command).Errors.Should().BeEmpty();
  }
}
