using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using GameBot.Domain.Commands;
using Xunit;
using static GameBot.IntegrationTests.Sequences.OcrTextRunKit;

#pragma warning disable CA2007, CA1826

namespace GameBot.IntegrationTests.Sequences;

/// <summary>The run log shows the text that an ocrText condition read (issue #338, User Story 4, SC-004).</summary>
public sealed class OcrTextConditionRunLogTests {
  [Fact]
  public async Task TheLogLineHasTheTextReadTheExpectedTextAndTheResult() {
    var reader = new FakeReader { Text = "0" };

    var (result, _) = await RunAsync(reader, "sess-1", false, If(0, "check", Expect("0")));

    result.Steps.First().Message.Should().Contain("ocrText: read='0' expected='0' result=true");
  }

  [Fact]
  public async Task APatternShowsInTheLogLine() {
    var reader = new FakeReader { Text = "left: 3" };

    var (result, _) = await RunAsync(reader, "sess-1", false, If(0, "check", Match(@"left: 0$")));

    result.Steps.First().Message.Should().Contain(@"ocrText: read='left: 3' pattern='left: 0$' result=false");
  }

  [Fact]
  public async Task AFailureShowsItsReasonCode() {
    var reader = new FakeReader { Reason = "ocr-error" };

    var (result, _) = await RunAsync(reader, "sess-1", false, If(0, "check", Expect("0")));

    result.Steps.First().Message.Should().Contain("result=false").And.Contain("reason=ocr-error");
  }

  [Fact]
  public async Task APatternTimeOutShowsTheNoteAndNoReasonCode() {
    var reader = new FakeReader { Text = new string('a', 60) + "!" };

    var (result, _) = await RunAsync(reader, "sess-1", false, If(0, "check", Match(@"^(a+)+$")));

    var message = result.Steps.First().Message;
    message.Should().Contain("pattern timed out").And.NotContain("reason=");
  }

  [Fact]
  public async Task EachEvaluatedChildOfACompositeHasALineAndASkippedChildHasNone() {
    var reader = new FakeReader { Text = "3" };
    var composite = new AllStepCondition {
      Children = new SequenceStepCondition[] { Expect("0"), Expect("3") }
    };

    var (result, _) = await RunAsync(reader, "sess-1", false, If(0, "check", composite));

    // The first child is false, so "all" stops and the second child gets no read and no line.
    var message = result.Steps.First().Message!;
    message.Should().Contain("expected='0'").And.NotContain("expected='3'");
    reader.Sessions.Should().HaveCount(1);
  }

  [Fact]
  public async Task BothChildrenOfAnAnyAreLoggedWhenNeitherSettlesIt() {
    var reader = new FakeReader { Text = "3" };
    var composite = new AnyStepCondition {
      Children = new SequenceStepCondition[] { Expect("0"), Expect("1") }
    };

    var (result, _) = await RunAsync(reader, "sess-1", false, If(0, "check", composite));

    var message = result.Steps.First().Message!;
    message.Should().Contain("expected='0'").And.Contain("expected='1'");
  }

  [Fact]
  public async Task AGuardThatSkipsAStepShowsTheLine() {
    var reader = new FakeReader { Text = "3" };

    var (result, executed) = await RunAsync(reader, "sess-1", false, Cmd(0, "guarded", Expect("0")));

    executed.Should().BeEmpty();
    result.Steps.Single().Message.Should().Contain("ocrText: read='3' expected='0' result=false");
  }
}
