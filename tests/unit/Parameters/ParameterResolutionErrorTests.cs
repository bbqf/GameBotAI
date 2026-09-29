using FluentAssertions;
using GameBot.Domain.Parameters;
using Xunit;

namespace GameBot.UnitTests.Parameters;

/// <summary>
/// Feature 115: the message of an unresolved step binding value tells how to supply the value. The other
/// messages do not change.
/// </summary>
public sealed class ParameterResolutionErrorTests {
  [Fact]
  public void UnresolvedBindingValueGivesTheMessageWithTheHint() {
    var error = new ParameterResolutionError(
        "novaOptionImage", "parameterBindings.novaOptionImage", ParameterResolutionReasons.Unresolved);

    error.ToMessage("s1").Should().Be(
        "Step 's1': parameter 'novaOptionImage' used by field 'parameterBindings.novaOptionImage' could not be resolved from any scope. "
        + "Do one of these to supply a value for 'novaOptionImage'. Supply the value in the queue template entry or in the run request. "
        + "Give 'novaOptionImage' a default value in the sequence or in the calling command. "
        + "Bind a literal value, or bind a value in the calling command.");
  }

  [Fact]
  public void UnresolvedCommandFieldGivesTheMessageWithNoHint() {
    var error = new ParameterResolutionError(
        "novaOptionImage", "primitiveTap.detectionTarget.referenceImageId", ParameterResolutionReasons.Unresolved);

    error.ToMessage("2").Should().Be(
        "Step '2': parameter 'novaOptionImage' used by field 'primitiveTap.detectionTarget.referenceImageId' could not be resolved from any scope.");
  }

  [Fact]
  public void NotANumberGivesTheMessageWithNoHint() {
    var error = new ParameterResolutionError(
        "count", "parameterBindings.count", ParameterResolutionReasons.NotANumber, "abc");

    error.ToMessage("s1").Should().Be(
        "Step 's1': parameter 'count' resolved to 'abc', which is not a whole number for field 'parameterBindings.count'.");
  }
}
