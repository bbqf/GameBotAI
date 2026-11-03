using System.Text.Json;
using FluentAssertions;
using GameBot.Domain.Commands;
using GameBot.Domain.Services;
using Xunit;

namespace GameBot.UnitTests.Sequences;

/// <summary>Feature 175: the save check of the <c>until</c> and <c>absentCaptures</c> options.</summary>
public sealed class WaitForImageUntilValidationTests {
  private static IReadOnlyList<string> Validate(Action<Dictionary<string, object?>> configure) {
    var action = new SequenceActionPayload { Type = "WaitForImage" };
    action.Parameters["timeoutMs"] = 1000;
    configure(action.Parameters);
    var step = new SequenceStep { StepId = "wait", Action = action, StepType = SequenceStepType.Action };
    return new SequenceStepValidationService().Validate(new[] { step });
  }

  [Fact]
  public void NoOptionsAreValid() {
    Validate(_ => { }).Should().BeEmpty();
  }

  [Theory]
  [InlineData("present")]
  [InlineData("absent")]
  [InlineData("ABSENT")]
  [InlineData("Present")]
  public void KnownUntilValuesInAnyCaseAreValid(string until) {
    Validate(p => p["until"] = until).Should().BeEmpty();
  }

  [Fact]
  public void KnownUntilAsJsonElementIsValid() {
    Validate(p => p["until"] = JsonSerializer.SerializeToElement("absent")).Should().BeEmpty();
  }

  [Fact]
  public void InvalidUntilGivesAClearError() {
    Validate(p => p["until"] = "gone").Should().ContainSingle()
      .Which.Should().Be("Step 'wait' waitForImage until must be 'present' or 'absent'.");
  }

  [Fact]
  public void InvalidUntilAsJsonElementGivesAnError() {
    Validate(p => p["until"] = JsonSerializer.SerializeToElement("later")).Should().ContainSingle();
  }

  [Theory]
  [InlineData(0)]
  [InlineData(-1)]
  public void AbsentCapturesBelowOneGivesAnError(int value) {
    Validate(p => p["absentCaptures"] = value).Should().ContainSingle()
      .Which.Should().Be("Step 'wait' waitForImage absentCaptures must be an integer of 1 or more.");
  }

  [Fact]
  public void AbsentCapturesThatIsNotAWholeNumberGivesAnError() {
    Validate(p => p["absentCaptures"] = JsonSerializer.SerializeToElement(1.5)).Should().ContainSingle();
    Validate(p => p["absentCaptures"] = JsonSerializer.SerializeToElement("many")).Should().ContainSingle();
  }

  [Fact]
  public void AbsentCapturesOfOneOrMoreIsValid() {
    Validate(p => {
      p["until"] = "absent";
      p["absentCaptures"] = JsonSerializer.SerializeToElement(3);
    }).Should().BeEmpty();
    Validate(p => p["absentCaptures"] = 1).Should().BeEmpty();
  }

  [Fact]
  public void AbsentCapturesWithPresentModeIsAccepted() {
    Validate(p => {
      p["until"] = "present";
      p["absentCaptures"] = 3;
    }).Should().BeEmpty();
  }

  [Fact]
  public void TemplateReferencesAreSkipped() {
    var errors = Validate(p => {
      p["until"] = "{{mode}}";
      p["absentCaptures"] = "{{captures}}";
    });
    errors.Should().NotContain(e => e.Contains("until must be", StringComparison.Ordinal));
    errors.Should().NotContain(e => e.Contains("absentCaptures must be", StringComparison.Ordinal));
  }

  [Fact]
  public void BothErrorsAreListed() {
    Validate(p => {
      p["until"] = "gone";
      p["absentCaptures"] = 0;
    }).Should().HaveCount(2);
  }
}
