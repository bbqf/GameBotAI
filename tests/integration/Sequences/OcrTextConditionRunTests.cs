using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Xunit;
using static GameBot.IntegrationTests.Sequences.OcrTextRunKit;

#pragma warning disable CA2007, CA1826

namespace GameBot.IntegrationTests.Sequences;

/// <summary>An If step that branches on the text read from the screen (issue #338, User Story 1).</summary>
public sealed class OcrTextConditionRunTests {
  [Theory]
  [InlineData("0", "then")]
  [InlineData("3", "else")]
  public async Task AnIfStepTakesTheBranchThatMatchesTheText(string screenText, string expectedBranch) {
    var reader = new FakeReader { Text = screenText };

    var (result, executed) = await RunAsync(reader, "sess-1", false, If(0, "check", Expect("0")));

    result.Status.Should().Be("Succeeded");
    executed.Should().Equal(expectedBranch);
    reader.Sessions.Should().Equal("sess-1");
  }

  [Fact]
  public async Task APatternMatchesTheEndOfTheText() {
    var reader = new FakeReader { Text = "Interaction attempts left: 0" };

    var (_, executed) = await RunAsync(reader, "sess-1", false, If(0, "check", Match(@"left: 0$")));

    executed.Should().Equal("then");
  }

  [Fact]
  public async Task NothingBeyondTheBranchCommandsReachesTheInputSink() {
    // FR-011: the condition only reads. The only commands that run are the ones of the chosen branch.
    var reader = new FakeReader { Text = "0" };

    var (_, executed) = await RunAsync(reader, "sess-1", false, If(0, "check", Expect("0")));

    executed.Should().ContainSingle().Which.Should().Be("then");
  }

  [Fact]
  public async Task ARunWithNoSessionIsFalseWithTheReasonNoSessionAndDoesNotCrash() {
    var reader = new FakeReader { Text = "0" };

    var (result, executed) = await RunAsync(reader, null, false, If(0, "check", Expect("0")));

    result.Status.Should().Be("Succeeded");
    executed.Should().Equal("else");
    reader.Sessions.Should().BeEmpty();
    result.Steps.First().Message.Should().Contain("reason=no-session");
  }

  [Fact]
  public async Task AReaderFailureTakesTheElseBranchAndTheRunContinues() {
    var reader = new FakeReader { Reason = "capture-failed" };

    var (result, executed) = await RunAsync(reader, "sess-1", false, If(0, "check", Expect("0")), Cmd(1, "after"));

    result.Status.Should().Be("Succeeded");
    executed.Should().Equal("else", "after");
    result.Steps.First().Message.Should().Contain("reason=capture-failed");
  }
}
