using FluentAssertions;
using GameBot.Service.Services.Notifications;
using Xunit;

#pragma warning disable CA2007, CA1861, CA1707

namespace GameBot.UnitTests.Notifications;

/// <summary>Feature 120 (V-14): the plain text of a message.</summary>
public sealed class NotificationMessageFormatterTests {
  [Theory]
  [InlineData("Success", "\U0001F7E2", "success")]
  [InlineData("Recovered", "\U0001F7E2", "recovered")]
  [InlineData("Failure", "\U0001F534", "failure")]
  [InlineData("Cancelled", "\U0001F7E1", "cancelled")]
  public void V14_TextHasTheQueueTheSequenceTheCircleAndTheStatus(string statusName, string circle, string word) {
    var status = System.Enum.Parse<NotificationMessageStatus>(statusName);
    var text = NotificationMessageFormatter.Format("Farm-1", "q1", "PNS.CollectResources", "s1", status);

    text.Should().Be($"Farm-1 : PNS.CollectResources : {circle} {word}");
  }

  [Theory]
  [InlineData("my_queue*", "seq[1](x)")]
  [InlineData("`code`", "_a_ *b* [c]")]
  public void V14_SpecialCharactersStayAsTheyAre(string queue, string sequence) {
    var text = NotificationMessageFormatter.Format(queue, "q1", sequence, "s1", NotificationMessageStatus.Failure);

    text.Should().Be($"{queue} : {sequence} : \U0001F534 failure");
  }

  [Theory]
  [InlineData(null)]
  [InlineData("")]
  [InlineData("   ")]
  public void V14_AnAbsentNameIsReplacedByTheId(string? name) {
    var text = NotificationMessageFormatter.Format(name, "q-id", name, "s-id", NotificationMessageStatus.Cancelled);

    text.Should().Be("q-id : s-id : \U0001F7E1 cancelled");
  }

  [Fact]
  public void TheTestMessageIsFixed() {
    NotificationMessageFormatter.TestMessageText.Should().Be("GameBot test message");
  }
}
