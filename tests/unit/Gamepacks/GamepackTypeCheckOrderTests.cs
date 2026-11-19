using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using FluentAssertions;
using GameBot.Domain.Commands;
using GameBot.Domain.Gamepacks;
using Xunit;
using M = GameBot.UnitTests.Gamepacks.GamepackMemberOrderFixtures;

#pragma warning disable CA1707, CA2007, CA1861

namespace GameBot.UnitTests.Gamepacks;

/// <summary>
/// Feature 194, item D-5: the type member check must give the same result in each order. Before the repair, the check
/// found no problem when its JSON options had no type info resolver yet. The first typed read of the options adds the
/// resolver, so the result changed with the tests that ran before.
/// </summary>
public sealed class GamepackTypeCheckOrderTests {
  private static readonly (string Kind, Func<string> Condition, string Reason)[] BadTypes = {
    ("missing", M.MissingType, "the type member is missing"),
    ("unknown", M.UnknownType, "the type value is unknown"),
    ("number", M.NumberType, "the type value must be a string"),
    ("duplicate", M.DuplicateType, "the type member is duplicated")
  };

  // New options, as the serializer makes them: no type info resolver until the first typed read.
  private static JsonSerializerOptions NewSequenceOptions() {
    var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true, AllowOutOfOrderMetadataProperties = true };
    options.Converters.Add(new JsonStringEnumConverter());
    return options;
  }

  [Fact]
  public void TheCheckFindsEachBadTypeWithOptionsThatWereNotUsedBefore() {
    foreach (var (kind, condition, reason) in BadTypes) {
      using var doc = JsonDocument.Parse(condition());

      var problem = GamepackTypeDiscriminatorCheck.Find(doc.RootElement, typeof(SequenceStepCondition), NewSequenceOptions(), "$.condition");

      problem.Should().NotBeNull($"the case '{kind}' has a bad type member");
      problem!.Path.Should().Be("$.condition");
      problem.ReasonText.Should().Be(reason);
    }
  }

  [Fact]
  public void ParseGivesTheSameResultInBothOrders() {
    var forward = ParseAll(BadTypes);
    // Enumerable.Reverse: with C# 14, BadTypes.Reverse() selects the span method, which returns void.
    var reverse = ParseAll(Enumerable.Reverse(BadTypes));

    foreach (var (kind, _, reason) in BadTypes) {
      forward[kind].InvalidType.Should().BeTrue($"the case '{kind}' has a bad type member (forward order)");
      reverse[kind].InvalidType.Should().BeTrue($"the case '{kind}' has a bad type member (reverse order)");
      forward[kind].Message.Should().Contain("$.sequences[0].steps[1].condition").And.Contain(reason);
      reverse[kind].Message.Should().Be(forward[kind].Message);
    }
  }

  private static Dictionary<string, (bool InvalidType, string Message)> ParseAll(IEnumerable<(string Kind, Func<string> Condition, string Reason)> cases) {
    var results = new Dictionary<string, (bool, string)>();
    foreach (var (kind, condition, _) in cases) {
      GamepackFileSerializer.Parse(M.PackWithCondition(M.ConditionFirst)).InvalidType.Should().BeFalse();
      var result = GamepackFileSerializer.Parse(M.PackWithCondition(condition()));
      results[kind] = (result.InvalidType, string.Join(" | ", result.Problems.Select(p => p.Message)));
    }

    return results;
  }
}
