using FluentAssertions;
using GameBot.Service.Services.Notifications;
using Xunit;

#pragma warning disable CA2007, CA1707

namespace GameBot.UnitTests.Notifications;

/// <summary>Feature 126: the rules of the last-sent record.</summary>
public sealed class NotificationLastSentStateTests {
  private static LastSentMessage Msg(string queue = "q1", string key = "s1", string status = "failure", string text = "A")
    => new(queue, key, status, text);

  [Fact]
  public void IsSameIsFalseWithNoRecord() {
    new NotificationLastSentState().IsSame("t1", Msg()).Should().BeFalse();
  }

  [Fact]
  public void IsSameIsTrueAfterRecordOfAnEqualMessage() {
    var state = new NotificationLastSentState();
    state.Record("t1", Msg());

    state.IsSame("t1", Msg()).Should().BeTrue();
  }

  [Theory]
  [InlineData("q2", "s1", "failure", "A")]
  [InlineData("q1", "s2", "failure", "A")]
  [InlineData("q1", "s1", "success", "A")]
  [InlineData("q1", "s1", "failure", "B")]
  [InlineData("q1", "s1", "failure", "a")]
  public void IsSameIsFalseWhenOneFieldDiffers(string queue, string key, string status, string text) {
    var state = new NotificationLastSentState();
    state.Record("t1", Msg());

    state.IsSame("t1", Msg(queue, key, status, text)).Should().BeFalse();
  }

  [Fact]
  public void RecordsOfTwoTargetsAreSeparate() {
    var state = new NotificationLastSentState();
    state.Record("t1", Msg());

    state.IsSame("t2", Msg()).Should().BeFalse();
    state.Record("t2", Msg(text: "B"));
    state.IsSame("t1", Msg()).Should().BeTrue();
  }

  [Fact]
  public void RecordReplacesTheEarlierRecord() {
    var state = new NotificationLastSentState();
    state.Record("t1", Msg());
    state.Record("t1", Msg(status: "success", text: "B"));

    state.IsSame("t1", Msg()).Should().BeFalse();
    state.IsSame("t1", Msg(status: "success", text: "B")).Should().BeTrue();
  }
}
