#pragma warning disable CA1861 // Test data arrays are small and used one time.
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using GameBot.Domain.Commands;
using GameBot.Domain.Parameters;
using GameBot.Domain.QueueTemplates;
using GameBot.Domain.Services;
using Xunit;

namespace GameBot.UnitTests.Parameters;

/// <summary>Feature 179: validation of list values on a queue-template entry.</summary>
public sealed class ParameterValidationListTests {
  private static ParameterDeclaration Quests(bool required = false, int? min = null, int? max = null) => new() {
    Name = "quests", Kind = ParameterInputKind.Image, AllowedImages = new[] { "a", "b", "c" },
    Multiple = true, Required = required, MinItems = min, MaxItems = max
  };

  private static ParameterValidationResult Validate(ParameterDeclaration declaration, ParameterBinding binding) {
    var entry = new QueueTemplateEntry { SequenceId = "s" };
    entry.ParameterValues.Add(binding);
    var sequence = new CommandSequence { Id = "s", Name = "s" };
    sequence.Parameters.Add(declaration);
    return ParameterValidationService.ValidateTemplateEntry(entry, 0, sequence, new List<ParameterDeclaration>());
  }

  private static ParameterBinding List(params string[] items) => new() {
    Name = "quests",
    Value = items.Length == 0 ? null : ParameterListRules.Join(items),
    Values = items
  };

  [Fact]
  public void EmptyRequiredListIsAnError() {
    var result = Validate(Quests(required: true), List());
    result.Errors.Should().ContainSingle(e => e.Code == ParameterValidationCodes.EmptyList);
  }

  [Fact]
  public void EmptyOptionalListIsNotAnError() {
    Validate(Quests(), List()).Errors.Should().BeEmpty();
  }

  [Fact]
  public void ListBelowMinimumIsAnError() {
    Validate(Quests(min: 2), List("a")).Errors.Should().ContainSingle(e => e.Code == ParameterValidationCodes.ListOutOfLimits);
  }

  [Fact]
  public void ListAboveMaximumIsAnError() {
    Validate(Quests(max: 2), List("a", "b", "c")).Errors
        .Should().ContainSingle(e => e.Code == ParameterValidationCodes.ListOutOfLimits);
  }

  [Fact]
  public void ListInsideLimitsHasNoIssue() {
    var result = Validate(Quests(min: 1, max: 3), List("a", "b"));
    result.Errors.Should().BeEmpty();
    result.Warnings.Where(w => w.Code == ParameterValidationCodes.InvalidValue).Should().BeEmpty();
  }

  [Fact]
  public void EachNotAllowedItemGivesAWarning() {
    var result = Validate(Quests(), List("a", "x", "y"));
    result.Errors.Should().BeEmpty();
    result.Warnings.Count(w => w.Code == ParameterValidationCodes.InvalidValue).Should().Be(2);
  }

  [Fact]
  public void ScalarValueIsAListOfOneAndChecksTheMinimum() {
    var scalar = new ParameterBinding { Name = "quests", Value = "a" };
    Validate(Quests(min: 2), scalar).Errors.Should().ContainSingle(e => e.Code == ParameterValidationCodes.ListOutOfLimits);
    Validate(Quests(min: 1), scalar).Errors.Should().BeEmpty();
  }

  [Fact]
  public void PlaceholderItemSkipsTheLimitCheck() {
    var binding = new ParameterBinding { Name = "quests", Value = "{{other}}" };
    Validate(Quests(min: 2), binding).Errors.Should().BeEmpty();
  }

  [Fact]
  public void ManyItemsForOneValueParameterGiveAWarning() {
    var single = new ParameterDeclaration { Name = "quests", Kind = ParameterInputKind.Image, AllowedImages = new[] { "a", "b" } };
    var result = Validate(single, List("a", "b"));
    result.Warnings.Should().Contain(w => w.Code == ParameterValidationCodes.InvalidValue
        && w.Message.Contains("holds one value", System.StringComparison.Ordinal));
  }

  [Fact]
  public void EmptyListDoesNotSatisfyARequiredParameter() {
    var entry = new QueueTemplateEntry { SequenceId = "s" };
    entry.ParameterValues.Add(List());
    var sequence = new CommandSequence { Id = "s", Name = "s" };
    sequence.Parameters.Add(Quests(required: true));

    ParameterValidationService.FindUnsatisfiedRequired(entry, sequence, new List<ParameterDeclaration>(), new HashSet<string>())
        .Should().Equal("quests");
  }

  [Fact]
  public void ListSatisfiesARequiredParameter() {
    var entry = new QueueTemplateEntry { SequenceId = "s" };
    entry.ParameterValues.Add(List("a"));
    var sequence = new CommandSequence { Id = "s", Name = "s" };
    sequence.Parameters.Add(Quests(required: true));

    ParameterValidationService.FindUnsatisfiedRequired(entry, sequence, new List<ParameterDeclaration>(), new HashSet<string>())
        .Should().BeEmpty();
  }
}
