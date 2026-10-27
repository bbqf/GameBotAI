using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using GameBot.Domain.Commands;
using GameBot.Domain.Parameters;
using GameBot.Domain.QueueTemplates;
using GameBot.Domain.Services;
using Xunit;

namespace GameBot.UnitTests.Parameters;

/// <summary>
/// Feature 165: the save check of <c>ocrText.pattern</c> and <c>ocrText.expected</c> placeholders.
/// </summary>
public sealed class ParameterValidationOcrPatternTests {
  private static readonly PixelRegion Region = new(10, 20, 300, 400);

  private static OcrTextStepCondition OcrPattern(string pattern) => new() { Region = Region, Pattern = pattern };

  private static OcrTextStepCondition OcrExpected(string expected) => new() { Region = Region, Expected = expected };

  private static SequenceActionPayload TapAction() => new() { Type = "tap" };

  private static SequenceStep ActionStep(SequenceStepCondition condition, string stepId = "s3") => new() {
    Order = 0, StepId = stepId, StepType = SequenceStepType.Action, Action = TapAction(), Condition = condition
  };

  private static CommandSequence Sequence(IEnumerable<ParameterDeclaration> parameters, params SequenceStep[] steps) {
    var sequence = new CommandSequence { Id = "seq", Name = "Seq" };
    foreach (var parameter in parameters) sequence.Parameters.Add(parameter);
    sequence.SetSteps(steps);
    return sequence;
  }

  private static ParameterValidationResult Validate(CommandSequence sequence) =>
      ParameterValidationService.ValidateSequence(sequence, _ => null);

  private static ParameterDeclaration Declared(string name, string? defaultValue = null) =>
      new() { Name = name, Default = defaultValue };

  // ── Unknown names ───────────────────────────────────────────────────────────

  [Fact]
  public void UnknownNameInPatternIsUnresolvable() {
    var result = Validate(Sequence(Enumerable.Empty<ParameterDeclaration>(),
        ActionStep(OcrPattern("(?s)(Gathering.*){{{missing}}}"))));

    var error = result.Errors.Should().ContainSingle().Subject;
    error.Code.Should().Be(ParameterValidationCodes.UnresolvableReference);
    error.FieldPath.Should().Be("condition.pattern");
    error.ParameterName.Should().Be("missing");
    error.Message.Should().Be("Step 's3': 'missing' is not declared here and is not a queue built-in.");
  }

  [Fact]
  public void UnknownNameInExpectedIsUnresolvable() {
    var result = Validate(Sequence(Enumerable.Empty<ParameterDeclaration>(),
        ActionStep(OcrExpected("Level {{level}}"))));

    var error = result.Errors.Should().ContainSingle().Subject;
    error.Code.Should().Be(ParameterValidationCodes.UnresolvableReference);
    error.FieldPath.Should().Be("condition.expected");
    error.ParameterName.Should().Be("level");
  }

  [Fact]
  public void UnknownNameInACompositeChildHasTheChildPath() {
    var condition = new AnyStepCondition {
      Children = new SequenceStepCondition[] { OcrExpected("Level 5"), OcrPattern("{{missing}}") }
    };

    var error = Validate(Sequence(Enumerable.Empty<ParameterDeclaration>(), ActionStep(condition)))
        .Errors.Should().ContainSingle().Subject;
    error.FieldPath.Should().Be("condition.children[1].pattern");
  }

  [Fact]
  public void DoubleBraceDigitsWithNoParameterIsUnresolvable() {
    var error = Validate(Sequence(Enumerable.Empty<ParameterDeclaration>(),
        ActionStep(OcrPattern("(Gathering.*){{3}}")))).Errors.Should().ContainSingle().Subject;

    error.Code.Should().Be(ParameterValidationCodes.UnresolvableReference);
    error.ParameterName.Should().Be("3");
    error.FieldPath.Should().Be("condition.pattern");
  }

  [Fact]
  public void DeclaredNameGivesNoError() {
    var result = Validate(Sequence(new[] { Declared("marchTarget"), Declared("level") },
        ActionStep(OcrPattern("(?s)(Gathering.*){{{marchTarget}}}")),
        ActionStep(OcrExpected("Level {{level}}"), "s4")));

    result.Errors.Should().BeEmpty();
  }

  // ── iteration ───────────────────────────────────────────────────────────────

  [Fact]
  public void IterationIsLegalInALoopCondition() {
    var loop = new SequenceStep {
      Order = 0, StepId = "w1", StepType = SequenceStepType.Loop,
      Loop = new WhileLoopConfig { Condition = OcrPattern("Round {{iteration}}"), MaxIterations = 3 }
    };

    Validate(Sequence(Enumerable.Empty<ParameterDeclaration>(), loop)).Errors.Should().BeEmpty();
  }

  [Fact]
  public void IterationIsLegalInABreakConditionAndInABodyStepCondition() {
    var loop = new SequenceStep {
      Order = 0, StepId = "loop1", StepType = SequenceStepType.Loop,
      Loop = new CountLoopConfig { Count = 2 },
      Body = new[] {
        ActionStep(OcrExpected("Round {{iteration}}"), "tap"),
        new SequenceStep {
          Order = 1, StepId = "brk", StepType = SequenceStepType.Break,
          BreakCondition = OcrPattern("Round {{iteration}}")
        }
      }
    };

    Validate(Sequence(Enumerable.Empty<ParameterDeclaration>(), loop)).Errors.Should().BeEmpty();
  }

  [Fact]
  public void IterationIsNotLegalInATopLevelStepCondition() {
    var error = Validate(Sequence(Enumerable.Empty<ParameterDeclaration>(),
        ActionStep(OcrPattern("Round {{iteration}}")))).Errors.Should().ContainSingle().Subject;

    error.Code.Should().Be(ParameterValidationCodes.UnresolvableReference);
    error.FieldPath.Should().Be("condition.pattern");
  }

  // ── Pattern with the sequence defaults ──────────────────────────────────────

  [Fact]
  public void BadPatternWithDefaultsIsInvalidResolvedPattern() {
    var result = Validate(Sequence(new[] { Declared("group", "(") }, ActionStep(OcrPattern("{{group}}abc"))));

    var error = result.Errors.Should().ContainSingle().Subject;
    error.Code.Should().Be(ParameterValidationCodes.InvalidResolvedPattern);
    error.FieldPath.Should().Be("condition.pattern");
    error.ParameterName.Should().Be("group");
    error.Message.Should().StartWith(
        "Step 's3': 'condition.pattern' with the default values gives the pattern '(abc', which is not a valid regular expression: ");
    error.Message.Should().EndWith(". Change the default value or the pattern.");
  }

  [Fact]
  public void NameWithNoDefaultStopsTheCheck() {
    var result = Validate(Sequence(new[] { Declared("group", "("), Declared("tail") },
        ActionStep(OcrPattern("{{group}}{{tail}}"))));

    result.Errors.Should().BeEmpty();
  }

  [Fact]
  public void BuiltInNameStopsTheCheck() {
    var result = Validate(Sequence(new[] { Declared("group", "(") },
        ActionStep(OcrPattern("{{group}}{{" + ParameterNameRules.QueueEmulatorSerial + "}}"))));

    result.Errors.Should().BeEmpty();
  }

  [Fact]
  public void IterationStopsTheCheck() {
    var loop = new SequenceStep {
      Order = 0, StepId = "w1", StepType = SequenceStepType.Loop,
      Loop = new WhileLoopConfig { Condition = OcrPattern("{{group}}{{iteration}}"), MaxIterations = 3 }
    };

    Validate(Sequence(new[] { Declared("group", "(") }, loop)).Errors.Should().BeEmpty();
  }

  [Fact]
  public void PlaceholderInACharacterClassWithNoDefaultSaves() {
    Validate(Sequence(new[] { Declared("chars") }, ActionStep(OcrPattern("[{{chars}}]"))))
        .Errors.Should().BeEmpty();
  }

  [Fact]
  public void ValidPatternWithDefaultsGivesNoError() {
    Validate(Sequence(new[] { Declared("marchTarget", "3") },
        ActionStep(OcrPattern("(?s)(Gathering.*){{{marchTarget}}}")))).Errors.Should().BeEmpty();
  }

  [Fact]
  public void BadDefaultInExpectedIsNotCompiled() {
    Validate(Sequence(new[] { Declared("accountName", "(") }, ActionStep(OcrExpected("{{accountName}}"))))
        .Errors.Should().BeEmpty();
  }

  // ── Unused values ───────────────────────────────────────────────────────────

  [Fact]
  public void ParameterThatOnlyOcrFieldsUseIsNotUnused() {
    var sequence = Sequence(new[] { Declared("marchTarget"), Declared("level") },
        ActionStep(OcrPattern("(?s)(Gathering.*){{{marchTarget}}}")),
        ActionStep(OcrExpected("Level {{level}}"), "s4"));
    var entry = new QueueTemplateEntry { SequenceId = "seq" };
    entry.ParameterValues.Add(new ParameterBinding { Name = "marchTarget", Value = "2" });
    entry.ParameterValues.Add(new ParameterBinding { Name = "level", Value = "5" });

    var result = ParameterValidationService.ValidateTemplateEntry(entry, 0, sequence, new List<ParameterDeclaration>());

    result.Warnings.Should().NotContain(w => w.Code == ParameterValidationCodes.UnusedValue);
  }
}
