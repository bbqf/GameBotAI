using System;
using FluentAssertions;
using GameBot.Domain.Parameters;
using GameBot.Domain.Queues;
using Xunit;

namespace GameBot.UnitTests.Parameters;

/// <summary>
/// Feature 196 (#409): the <c>entry</c> layer of a scope has the key of its template entry. The run loop uses the key
/// to find the current values of the entry when a booking of <c>reschedule-self</c> fires.
/// </summary>
public sealed class ParameterScopeEntryKeyTests {
  private static readonly ParameterBinding[] SlotOne = { new() { Name = "slot", Value = "one" } };

  [Fact]
  public void ChildForEntryMakesAnEntryLayerWithTheKeyAndTheValues() {
    var scope = ParameterScope.Empty.ChildForEntry("e1", SlotOne);

    scope.LayerName.Should().Be(ParameterScopeLayers.Entry);
    scope.EntryKey.Should().Be("e1");
    scope.TryResolve("slot", out var value).Should().BeTrue();
    value.Text.Should().Be("one");
    value.OriginLayer.Should().Be(ParameterScopeLayers.Entry);
  }

  [Fact]
  public void ChildForEntrySkipsABindingWithANullValue() {
    var scope = ParameterScope.Empty.ChildForEntry("e1", new[] {
      new ParameterBinding { Name = "slot", Value = null },
      new ParameterBinding { Name = "other", Value = "x" }
    });

    scope.TryResolve("slot", out _).Should().BeFalse();
    scope.TryResolve("other", out _).Should().BeTrue();
  }

  [Theory]
  [InlineData(null)]
  [InlineData("")]
  public void ChildForEntryWithNoKeyThrows(string? key) {
    var act = () => ParameterScope.Empty.ChildForEntry(key!, SlotOne);

    act.Should().Throw<ArgumentException>();
  }

  [Fact]
  public void InnerLayersGiveTheKeyOfTheEntryLayer() {
    var entry = ParameterScope.Empty.ChildForEntry("e1", SlotOne);

    entry.Child(ParameterScopeLayers.Sequence, null, null).EntryKey.Should().Be("e1");
    entry.WithIteration(2).EntryKey.Should().Be("e1");
    entry.TryBindChild(ParameterScopeLayers.Command, null, out var command, out _).Should().BeTrue();
    command!.EntryKey.Should().Be("e1");
  }

  [Fact]
  public void AScopeWithNoEntryLayerHasNoKey() {
    ParameterScope.Empty.EntryKey.Should().BeNull();
    ParameterScope.FromQueue(new ExecutionQueue { Id = "q", EmulatorSerial = "emu-1" }).EntryKey.Should().BeNull();
    ParameterScope.Empty.Child(ParameterScopeLayers.Entry, SlotOne, null).EntryKey.Should().BeNull();
  }

  [Fact]
  public void AnEntryLayerWithNoValuesAddsNoName() {
    var queue = ParameterScope.FromQueue(new ExecutionQueue { Id = "q", EmulatorSerial = "emu-1" });

    var scope = queue.ChildForEntry("e1", null);

    scope.Describe().Should().BeEquivalentTo(queue.Describe());
    scope.ToSubstitutionContext().Should().BeEquivalentTo(queue.ToSubstitutionContext());
  }
}
