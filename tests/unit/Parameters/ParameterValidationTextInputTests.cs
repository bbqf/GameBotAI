using System.Collections.Generic;
using FluentAssertions;
using GameBot.Domain.Commands;
using GameBot.Domain.Parameters;
using GameBot.Domain.Services;
using Xunit;

namespace GameBot.UnitTests.Parameters;

/// <summary>Feature 192: the text key <c>textInput.text</c> of <c>fieldTemplates</c> and the rule for its value.</summary>
public sealed class ParameterValidationTextInputTests {
  private const string TextKey = "textInput.text";

  private static Command TextCommand(string? overlay, string inline = "", int order = 0) {
    var command = new Command { Id = "c", Name = "C" };
    command.Parameters.Add(new ParameterDeclaration { Name = "a" });
    command.Parameters.Add(new ParameterDeclaration { Name = "fighterBatch", Type = ParameterValueType.Number });
    command.Parameters.Add(new ParameterDeclaration { Name = "word", Pattern = "x{value}", Type = ParameterValueType.Number });
    command.Steps.Add(new CommandStep {
      Type = CommandStepType.TextInput,
      Order = order,
      TextInput = new TextInputConfig { Text = inline },
      FieldTemplates = overlay is null ? null : new Dictionary<string, string> { [TextKey] = overlay }
    });
    return command;
  }

  [Fact]
  public void TheTextKeyIsSupportedAndIsATextPath() {
    CommandStepFieldPaths.IsSupported(TextKey).Should().BeTrue();
    CommandStepFieldPaths.IsTextPath(TextKey).Should().BeTrue();
    CommandStepFieldPaths.IsImagePath(TextKey).Should().BeFalse();
    CommandStepFieldPaths.IsTextPath("keyInput.key").Should().BeFalse();
  }

  [Fact]
  public void TheTextKeyAcceptsOneWholePlaceholder() {
    ParameterValidationService.ValidateCommand(TextCommand("{{fighterBatch}}")).Errors.Should().BeEmpty();
  }

  [Theory]
  [InlineData("{{a}}0")]
  [InlineData("250")]
  [InlineData("{{a}}{{fighterBatch}}")]
  public void TheTextKeyRejectsAValueThatIsNotOneWholePlaceholder(string value) {
    var result = ParameterValidationService.ValidateCommand(TextCommand(value, order: 3));

    var error = result.Errors.Should().ContainSingle(
        e => e.Code == ParameterValidationCodes.InvalidFieldTemplateValue).Subject;
    error.FieldPath.Should().Be(TextKey);
    error.Message.Should().Be(
        $"Step 3: the value of '{TextKey}' must be one whole placeholder, for example {{{{name}}}}.");
  }

  [Fact]
  public void AParameterThatGivesTextIsPermittedInTheTextKey() {
    var result = ParameterValidationService.ValidateCommand(TextCommand("{{word}}"));

    result.Errors.Should().NotContain(e => e.Code == ParameterValidationCodes.TextParameterInNumericField);
  }

  [Fact]
  public void AnUndeclaredNameInTheTextKeyIsUnresolvable() {
    var result = ParameterValidationService.ValidateCommand(TextCommand("{{fighterBach}}"));

    result.Errors.Should().Contain(e => e.Code == ParameterValidationCodes.UnresolvableReference
        && e.FieldPath == TextKey && e.ParameterName == "fighterBach");
  }

  [Fact]
  public void AnUndeclaredInlineNameIsUnresolvable() {
    var result = ParameterValidationService.ValidateCommand(TextCommand(null, "{{fighterBach}}"));

    result.Errors.Should().Contain(e => e.Code == ParameterValidationCodes.UnresolvableReference
        && e.FieldPath == TextKey && e.ParameterName == "fighterBach");
  }
}
