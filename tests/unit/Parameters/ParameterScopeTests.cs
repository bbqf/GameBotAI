using System.Collections.ObjectModel;
using FluentAssertions;
using GameBot.Domain.Parameters;
using GameBot.Domain.Queues;
using Xunit;

namespace GameBot.UnitTests.Parameters;

/// <summary>
/// Feature 078: resolution precedence, ambient fall-through, queue built-ins and immutability.
/// </summary>
public sealed class ParameterScopeTests {
  private static Collection<ParameterBinding> Bind(string name, string? value) =>
      new() { new ParameterBinding { Name = name, Value = value } };

  private static Collection<ParameterDeclaration> Declare(
      string name, string? defaultValue = null, ParameterValueType type = ParameterValueType.Text) =>
      new() { new ParameterDeclaration { Name = name, Default = defaultValue, Type = type } };

  // ── Precedence (FR-009) ────────────────────────────────────────────────────

  [Fact]
  public void CallSiteBindingBeatsInheritedValue() {
    var scope = ParameterScope.Empty
        .Child(ParameterScopeLayers.Entry, Bind("adbSerial", "outer"), null)
        .Child(ParameterScopeLayers.Command, Bind("adbSerial", "inner"), null);

    scope.TryResolve("adbSerial", out var value).Should().BeTrue();
    value.Text.Should().Be("inner");
    value.OriginLayer.Should().Be(ParameterScopeLayers.Command);
  }

  [Fact]
  public void InheritedValueBeatsDeclaredDefault() {
    var scope = ParameterScope.Empty
        .Child(ParameterScopeLayers.Entry, Bind("waitMs", "9000"), null)
        .Child(ParameterScopeLayers.Command, null, Declare("waitMs", "5000"));

    scope.TryResolve("waitMs", out var value).Should().BeTrue();
    value.Text.Should().Be("9000");
    value.OriginLayer.Should().Be(ParameterScopeLayers.Entry);
  }

  [Fact]
  public void DeclaredDefaultAppliesWhenNothingSuppliesTheName() {
    var scope = ParameterScope.Empty.Child(ParameterScopeLayers.Command, null, Declare("waitMs", "5000"));

    scope.TryResolve("waitMs", out var value).Should().BeTrue();
    value.Text.Should().Be("5000");
    value.OriginLayer.Should().Be(ParameterScopeLayers.Default);
  }

  [Fact]
  public void UnknownNameDoesNotResolve() {
    var scope = ParameterScope.Empty.Child(ParameterScopeLayers.Entry, Bind("a", "1"), null);

    scope.TryResolve("b", out _).Should().BeFalse();
  }

  // ── Ambient fall-through without re-mapping (FR-014) ───────────────────────

  [Fact]
  public void NameFallsThroughIntermediateLayersWithoutBeingRedeclared() {
    // The entry supplies adbSerial; the sequence layer declares nothing at all; the command layer
    // declares it. This is the motivating "pass through with zero ceremony" case (FR-012a).
    var scope = ParameterScope.Empty
        .Child(ParameterScopeLayers.Entry, Bind("adbSerial", "emulator-5560"), null)
        .Child(ParameterScopeLayers.Sequence, null, null)
        .Child(ParameterScopeLayers.Command, null, Declare("adbSerial"));

    scope.TryResolve("adbSerial", out var value).Should().BeTrue();
    value.Text.Should().Be("emulator-5560");
    value.OriginLayer.Should().Be(ParameterScopeLayers.Entry);
  }

  // ── Inherit vs deliberate empty (spec Edge Cases) ──────────────────────────

  [Fact]
  public void NullBindingValueMeansInheritAndKeepsWalkingOutward() {
    var scope = ParameterScope.Empty
        .Child(ParameterScopeLayers.Entry, Bind("adbSerial", "outer"), null)
        .Child(ParameterScopeLayers.Command, Bind("adbSerial", null), null);

    scope.TryResolve("adbSerial", out var value).Should().BeTrue();
    value.Text.Should().Be("outer");
  }

  [Fact]
  public void EmptyStringIsARealValueAndStopsTheWalk() {
    var scope = ParameterScope.Empty
        .Child(ParameterScopeLayers.Entry, Bind("suffix", "outer"), null)
        .Child(ParameterScopeLayers.Command, Bind("suffix", string.Empty), null);

    scope.TryResolve("suffix", out var value).Should().BeTrue();
    value.Text.Should().BeEmpty();
  }

  // ── Case sensitivity ───────────────────────────────────────────────────────

  [Fact]
  public void NamesAreMatchedCaseSensitively() {
    var scope = ParameterScope.Empty.Child(ParameterScopeLayers.Entry, Bind("adbSerial", "x"), null);

    scope.TryResolve("adbserial", out _).Should().BeFalse();
  }

  // ── Queue built-ins (FR-010, FR-011) ───────────────────────────────────────

  [Fact]
  public void QueueBuiltInsExposeSerialInstanceAndGame() {
    var queue = new ExecutionQueue {
      Id = "q1",
      Name = "Q",
      EmulatorSerial = "emulator-5558",
      EmulatorInstanceName = "PNS-1",
      EmulatorInstanceIndex = 2,
      LinkedGameId = "game-7"
    };

    var scope = ParameterScope.FromQueue(queue);

    scope.TryResolve(ParameterNameRules.QueueEmulatorSerial, out var serial).Should().BeTrue();
    serial.Text.Should().Be("emulator-5558");
    serial.OriginLayer.Should().Be(ParameterScopeLayers.Queue);
    scope.TryResolve(ParameterNameRules.QueueInstanceName, out var name).Should().BeTrue();
    name.Text.Should().Be("PNS-1");
    scope.TryResolve(ParameterNameRules.QueueInstanceIndex, out var index).Should().BeTrue();
    index.Text.Should().Be("2");
    scope.TryResolve(ParameterNameRules.QueueGameId, out var game).Should().BeTrue();
    game.Text.Should().Be("game-7");
  }

  [Fact]
  public void UnsetQueueFieldIsAbsentFromScopeRatherThanEmpty() {
    var queue = new ExecutionQueue { Id = "q1", Name = "Q", EmulatorSerial = "emulator-5558" };

    var scope = ParameterScope.FromQueue(queue);

    scope.TryResolve(ParameterNameRules.QueueInstanceName, out _).Should().BeFalse();
    scope.TryResolve(ParameterNameRules.QueueInstanceIndex, out _).Should().BeFalse();
    scope.TryResolve(ParameterNameRules.QueueGameId, out _).Should().BeFalse();
  }

  [Fact]
  public void NullQueueYieldsEmptyScope() {
    ParameterScope.FromQueue(null).Should().BeSameAs(ParameterScope.Empty);
  }

  // ── Loop composition (FR-008) ──────────────────────────────────────────────

  [Fact]
  public void IterationLayerComposesWithParameters() {
    var scope = ParameterScope.Empty
        .Child(ParameterScopeLayers.Entry, Bind("adbSerial", "emulator-5558"), null)
        .WithIteration(4);

    scope.TryResolve(ParameterNameRules.IterationName, out var iteration).Should().BeTrue();
    iteration.Text.Should().Be("4");
    scope.TryResolve("adbSerial", out var serial).Should().BeTrue();
    serial.Text.Should().Be("emulator-5558");
  }

  // ── Immutability (the queue run loop and monitor read concurrently) ────────

  [Fact]
  public void ChildDoesNotMutateItsParent() {
    var parent = ParameterScope.Empty.Child(ParameterScopeLayers.Entry, Bind("a", "1"), null);

    var child = parent.Child(ParameterScopeLayers.Command, Bind("b", "2"), null);

    child.TryResolve("b", out _).Should().BeTrue();
    parent.TryResolve("b", out _).Should().BeFalse();
    parent.Should().NotBeSameAs(child);
  }

  // ── Describe() / substitution context ──────────────────────────────────────

  [Fact]
  public void DescribeReportsEffectiveValueAndOriginLayer() {
    var scope = ParameterScope.Empty
        .Child(ParameterScopeLayers.Entry, Bind("adbSerial", "emulator-5560"), null)
        .Child(ParameterScopeLayers.Command, null, Declare("waitMs", "5000"));

    var described = scope.Describe();

    described.Should().ContainSingle(e => e.Name == "adbSerial"
        && e.Value == "emulator-5560" && e.OriginLayer == ParameterScopeLayers.Entry);
    described.Should().ContainSingle(e => e.Name == "waitMs"
        && e.Value == "5000" && e.OriginLayer == ParameterScopeLayers.Default && e.Declared);
  }

  [Fact]
  public void DescribeReportsDeclaredButUnsuppliedNameWithNullValue() {
    var scope = ParameterScope.Empty.Child(ParameterScopeLayers.Command, null, Declare("adbSerial"));

    scope.Describe().Should().ContainSingle(e => e.Name == "adbSerial" && e.Value == null && e.Declared);
  }

  [Fact]
  public void SubstitutionContextFlattensInnermostWins() {
    var scope = ParameterScope.Empty
        .Child(ParameterScopeLayers.Entry, Bind("a", "outer"), null)
        .Child(ParameterScopeLayers.Command, Bind("a", "inner"), null);

    scope.ToSubstitutionContext()["a"].Should().Be("inner");
  }

  [Fact]
  public void EmptyScopeHasNoSubstitutions() {
    ParameterScope.Empty.ToSubstitutionContext().Should().BeEmpty();
  }
}
