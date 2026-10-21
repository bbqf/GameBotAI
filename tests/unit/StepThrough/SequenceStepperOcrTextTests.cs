using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using GameBot.Domain.Commands;
using GameBot.Domain.Services.StepThrough;
using Xunit;
using static GameBot.UnitTests.StepThrough.StepperTestKit;

#pragma warning disable CA2007, CA1861, CA1859, CA1849

namespace GameBot.UnitTests.StepThrough;

/// <summary>
/// A step-through does not read the screen. An ocrText condition has no scope there, so it is false (the same as
/// lastRun), and the entry says that it is not evaluated (issue #338).
/// </summary>
public sealed class SequenceStepperOcrTextTests {
  private static OcrTextStepCondition OcrText() => new() {
    Region = new PixelRegion(1, 1, 10, 10),
    Expected = "0"
  };

  [Fact]
  public async Task TheGuardIsFalseAndTheEntryNotesThatItIsNotEvaluated() {
    var sequence = Sequence(Cmd(0, "a", OcrText()), Cmd(1, "b"));
    var executed = new List<string>();
    var state = Start(sequence);
    var stepper = new SequenceStepper(Runner(sequence));

    var produced = await stepper.RunNextAsync(sequence, state, Deps(executed));

    var entry = produced.Should().ContainSingle().Subject;
    entry.Status.Should().Be("Skipped");
    entry.Notes.Should().Contain(SequenceStepper.OcrTextNote);
    SequenceStepper.OcrTextNote.Should().Be("ocrText is not evaluated");
    executed.Should().BeEmpty();
  }

  [Fact]
  public async Task AnOcrTextInsideACompositeGuardAlsoGetsTheNote() {
    var composite = new AllStepCondition { Children = new SequenceStepCondition[] { Image("x"), OcrText() } };
    var sequence = Sequence(Cmd(0, "a", composite));
    var state = Start(sequence);
    var stepper = new SequenceStepper(Runner(sequence));

    var produced = await stepper.RunNextAsync(sequence, state, Deps(new List<string>(), Answers(("x", new[] { true }))));

    produced.Single().Notes.Should().Contain(SequenceStepper.OcrTextNote);
  }

  [Fact]
  public async Task AStepWithoutAnOcrTextConditionHasNoNote() {
    var sequence = Sequence(Cmd(0, "a", Image("x")));
    var state = Start(sequence);
    var stepper = new SequenceStepper(Runner(sequence));

    var produced = await stepper.RunNextAsync(sequence, state, Deps(new List<string>(), Answers(("x", new[] { true }))));

    produced.Single().Notes.Should().NotContain(SequenceStepper.OcrTextNote);
  }
}
