using FluentAssertions;
using GameBot.Domain.Updates;
using GameBot.Service.Services.Updates;
using Xunit;

namespace GameBot.UnitTests.Updates;

public sealed class UpdateFailureExceptionTests {
  [Fact]
  public void DefaultConstructorGivesAGenericError() {
    var ex = new UpdateFailureException();

    ex.Error.Code.Should().Be("update_failed");
    ex.Error.Message.Should().NotBeNullOrWhiteSpace();
  }

  [Fact]
  public void MessageConstructorKeepsTheMessage() {
    var ex = new UpdateFailureException("it failed");

    ex.Message.Should().Be("it failed");
    ex.Error.Message.Should().Be("it failed");
    ex.Error.Code.Should().Be("update_failed");
  }

  [Fact]
  public void MessageAndInnerConstructorKeepsBoth() {
    var inner = new IOException("disk");

    var ex = new UpdateFailureException("it failed", inner);

    ex.InnerException.Should().BeSameAs(inner);
    ex.Error.Message.Should().Be("it failed");
  }

  [Fact]
  public void ErrorConstructorKeepsTheCodeAndTheHint() {
    var ex = new UpdateFailureException(new UpdateError("update_download_failed", "stopped", "try again"));

    ex.Error.Code.Should().Be("update_download_failed");
    ex.Error.Hint.Should().Be("try again");
    ex.Message.Should().Be("stopped");
  }

  [Fact]
  public void ErrorAndInnerConstructorKeepsBoth() {
    var inner = new InvalidOperationException("x");

    var ex = new UpdateFailureException(new UpdateError("c", "m", null), inner);

    ex.InnerException.Should().BeSameAs(inner);
    ex.Error.Code.Should().Be("c");
  }

  [Fact]
  public void NullErrorFallsBackToAGenericError() {
    var ex = new UpdateFailureException((UpdateError)null!);
    var withInner = new UpdateFailureException((UpdateError)null!, new IOException());

    ex.Error.Code.Should().Be("update_failed");
    withInner.Error.Code.Should().Be("update_failed");
  }
}
