#pragma warning disable CA1861 // Test data arrays are small and used one time.
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using FluentAssertions;
using GameBot.Domain.Commands;
using GameBot.Domain.Parameters;
using GameBot.Domain.QueueTemplates;
using GameBot.Domain.Services;
using Xunit;

namespace GameBot.UnitTests.Parameters;

/// <summary>Feature 168 (FR-012): a value that does not fit its declaration is a warning, never an error.</summary>
public sealed class ParameterValidationFriendlyValueTests {
  private static readonly ParameterDeclaration Speed = new() {
    Name = "speed", Type = ParameterValueType.Number, Min = 1, Max = 9
  };

  private static readonly ParameterDeclaration Mode = new() {
    Name = "mode", Kind = ParameterInputKind.Choice,
    Options = new[] { new ParameterOption { Label = "Fast", Value = "f" } }
  };

  private static readonly ParameterDeclaration Pic = new() {
    Name = "pic", Kind = ParameterInputKind.Image, AllowedImages = new[] { "a" }
  };

  private static QueueTemplateEntry Entry(string name, string? value) {
    var entry = new QueueTemplateEntry { SequenceId = "s1" };
    entry.ParameterValues.Add(new ParameterBinding { Name = name, Value = value });
    return entry;
  }

  private static CommandSequence SequenceWith(params ParameterDeclaration[] declarations) {
    var sequence = new CommandSequence { Id = "s1", Name = "S" };
    foreach (var d in declarations) sequence.Parameters.Add(d);
    return sequence;
  }

  [Fact]
  public void TemplateEntryValueOutOfRangeGivesWarningNotError() {
    var result = ParameterValidationService.ValidateTemplateEntry(
        Entry("speed", "12"), 0, SequenceWith(Speed), new List<ParameterDeclaration>());

    result.Errors.Should().BeEmpty();
    result.Warnings.Should().ContainSingle(w => w.Code == ParameterValidationCodes.InvalidValue && w.ParameterName == "speed");
  }

  [Fact]
  public void TemplateEntryValueNotAnOptionGivesWarning() {
    var result = ParameterValidationService.ValidateTemplateEntry(
        Entry("mode", "zzz"), 0, SequenceWith(Mode), new List<ParameterDeclaration>());

    result.Warnings.Should().Contain(w => w.Code == ParameterValidationCodes.InvalidValue);
  }

  [Fact]
  public void TemplateEntryValueThatFitsGivesNoWarning() {
    ParameterValidationService.ValidateTemplateEntry(
        Entry("mode", "f"), 0, SequenceWith(Mode), new List<ParameterDeclaration>())
        .Warnings.Should().NotContain(w => w.Code == ParameterValidationCodes.InvalidValue);
  }

  [Fact]
  public void PlaceholderAndInheritValuesAreNotChecked() {
    ParameterValidationService.ValidateTemplateEntry(
        Entry("speed", "{{other}}"), 0, SequenceWith(Speed), new List<ParameterDeclaration>())
        .Warnings.Should().BeEmpty();
    ParameterValidationService.ValidateTemplateEntry(
        Entry("speed", null), 0, SequenceWith(Speed), new List<ParameterDeclaration>())
        .Warnings.Should().BeEmpty();
  }

  [Fact]
  public void MissingStoredImageGivesWarning() {
    var result = ParameterValidationService.ValidateTemplateEntry(
        Entry("pic", "a"), 0, SequenceWith(Pic), new List<ParameterDeclaration>(), null, _ => false);

    result.Warnings.Should().Contain(w => w.Code == ParameterValidationCodes.InvalidValue && w.Message.Contains("not stored"));
  }

  [Fact]
  public void SequenceStepBindingOutOfRangeGivesWarning() {
    var sequence = SequenceWith();
    sequence.SetSteps(new[] {
      new SequenceStep {
        Order = 0, StepId = "st", StepType = SequenceStepType.Command, CommandId = "cmd",
        ParameterBindings = new Collection<ParameterBinding> { new() { Name = "speed", Value = "99" } }
      }
    });

    var result = ParameterValidationService.ValidateSequence(
        sequence, id => id == "cmd" ? new List<ParameterDeclaration> { Speed } : null);

    result.Errors.Should().BeEmpty();
    result.Warnings.Should().ContainSingle(w => w.Code == ParameterValidationCodes.InvalidValue);
  }

  [Fact]
  public void CommandStepBindingNotAnOptionGivesWarning() {
    var command = new Command { Id = "c1", Name = "Outer" };
    command.Steps.Add(new CommandStep {
      Type = CommandStepType.Command, Order = 0, TargetId = "inner",
      ParameterBindings = new Collection<ParameterBinding> { new() { Name = "mode", Value = "zzz" } }
    });

    var result = ParameterValidationService.ValidateCommand(
        command, id => id == "inner" ? new List<ParameterDeclaration> { Mode } : null);

    result.Warnings.Should().Contain(w => w.Code == ParameterValidationCodes.InvalidValue);
  }

  [Fact]
  public void UnknownAllowedImageGivesWarning() {
    var sequence = SequenceWith(Pic);

    var result = ParameterValidationService.ValidateSequence(sequence, _ => null, _ => false);

    result.Warnings.Should().Contain(w => w.Code == ParameterValidationCodes.UnknownAllowedImage);
    result.Errors.Should().BeEmpty();
  }

  [Fact]
  public void ValidationWithoutImageCheckSkipsImageWarnings() {
    ParameterValidationService.ValidateSequence(SequenceWith(Pic), _ => null)
        .Warnings.Should().BeEmpty();
  }
}
