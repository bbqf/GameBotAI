using System.Text.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using GameBot.Domain.Commands;
using Xunit;

namespace GameBot.UnitTests.Sequences;

/// <summary>
/// Polymorphic round-tripping for composite conditions (feature 088).
/// <para>
/// This is deliberately the first thing proven. The condition hierarchy carries a subtle trap the
/// file itself documents: the <c>type</c> discriminator and an un-ignored <c>Type</c> property both
/// serialize under the same name once the web naming policy lowercases it, producing a duplicate key
/// that then fails to read back. Getting that wrong on a new subclass would surface much later as an
/// unreadable stored sequence.
/// </para>
/// </summary>
public sealed class CompositeConditionSerializationTests {
  private static readonly JsonSerializerOptions WebOptions = new(JsonSerializerDefaults.Web);

  [Theory]
  [InlineData("all")]
  [InlineData("any")]
  [InlineData("none")]
  public void EachRuleSerializesUnderItsOwnDiscriminator(string rule) {
    CompositeStepCondition condition = rule switch {
      "any" => new AnyStepCondition { Children = new[] { Image("a") } },
      "none" => new NoneStepCondition { Children = new[] { Image("a") } },
      _ => new AllStepCondition { Children = new[] { Image("a") } }
    };

    var json = JsonSerializer.Serialize<SequenceStepCondition>(condition, WebOptions);

    JsonNode.Parse(json)!["type"]!.GetValue<string>().Should().Be(rule);
  }

  [Theory]
  [InlineData("all")]
  [InlineData("any")]
  [InlineData("none")]
  public void EachRuleRoundTripsBackToItsOwnType(string rule) {
    SequenceStepCondition original = rule switch {
      "any" => new AnyStepCondition { Children = new[] { Image("a") } },
      "none" => new NoneStepCondition { Children = new[] { Image("a") } },
      _ => new AllStepCondition { Children = new[] { Image("a") } }
    };

    var json = JsonSerializer.Serialize(original, WebOptions);
    var restored = JsonSerializer.Deserialize<SequenceStepCondition>(json, WebOptions);

    restored.Should().BeOfType(original.GetType());
    restored!.Type.Should().Be(rule);
  }

  [Fact]
  public void TheTypePropertyIsNotWrittenTwice() {
    var json = JsonSerializer.Serialize<SequenceStepCondition>(
      new AllStepCondition { Children = new[] { Image("a") } }, WebOptions);

    // A duplicate key would make this throw with "duplicate 'type' metadata property".
    var reread = () => JsonSerializer.Deserialize<SequenceStepCondition>(json, WebOptions);
    reread.Should().NotThrow();

    // Exactly one "type" per condition in the tree — the composite and its single child — so a
    // subclass that forgot [JsonIgnore] on its Type override would push this to three.
    System.Text.RegularExpressions.Regex.Matches(json, "\"type\"").Should().HaveCount(2);
  }

  [Fact]
  public void ANestedCompositeRoundTripsWithChildrenOrderAndSettingsIntact() {
    var original = new AllStepCondition {
      Children = new SequenceStepCondition[] {
        new ImageVisibleStepCondition { ImageId = "confirm", MinSimilarity = 0.85 },
        new NoneStepCondition {
          Children = new SequenceStepCondition[] {
            new ImageVisibleStepCondition { ImageId = "gas-title", Negate = true },
            new CommandOutcomeStepCondition { StepRef = "probe", ExpectedState = "success" }
          }
        }
      }
    };

    var json = JsonSerializer.Serialize<SequenceStepCondition>(original, WebOptions);
    var restored = JsonSerializer.Deserialize<SequenceStepCondition>(json, WebOptions);

    var composite = restored.Should().BeOfType<AllStepCondition>().Subject;
    composite.Children.Should().HaveCount(2);

    var firstChild = composite.Children[0].Should().BeOfType<ImageVisibleStepCondition>().Subject;
    firstChild.ImageId.Should().Be("confirm");
    firstChild.MinSimilarity.Should().Be(0.85);

    var nested = composite.Children[1].Should().BeOfType<NoneStepCondition>().Subject;
    nested.Children.Should().HaveCount(2);
    nested.Children[0].Should().BeOfType<ImageVisibleStepCondition>().Which.Negate.Should().BeTrue();
    nested.Children[1].Should().BeOfType<CommandOutcomeStepCondition>().Which.StepRef.Should().Be("probe");
  }

  [Fact]
  public void NegateOnACompositeRoundTrips() {
    var json = JsonSerializer.Serialize<SequenceStepCondition>(
      new AnyStepCondition { Children = new[] { Image("a") }, Negate = true }, WebOptions);

    JsonSerializer.Deserialize<SequenceStepCondition>(json, WebOptions)!.Negate.Should().BeTrue();
  }

  [Fact]
  public void ExistingLeafConditionsStillRoundTripUnchanged() {
    // FR-015: the two pre-existing forms must be untouched by this change.
    var image = JsonSerializer.Serialize<SequenceStepCondition>(
      new ImageVisibleStepCondition { ImageId = "a", MinSimilarity = 0.9 }, WebOptions);
    var outcome = JsonSerializer.Serialize<SequenceStepCondition>(
      new CommandOutcomeStepCondition { StepRef = "probe", ExpectedState = "success" }, WebOptions);

    JsonNode.Parse(image)!["type"]!.GetValue<string>().Should().Be("imageVisible");
    JsonNode.Parse(outcome)!["type"]!.GetValue<string>().Should().Be("commandOutcome");
    JsonSerializer.Deserialize<SequenceStepCondition>(image, WebOptions).Should().BeOfType<ImageVisibleStepCondition>();
    JsonSerializer.Deserialize<SequenceStepCondition>(outcome, WebOptions).Should().BeOfType<CommandOutcomeStepCondition>();
  }

  [Fact]
  public void ALastRunConditionWithSinceRoundTrips() {
    var json = JsonSerializer.Serialize<SequenceStepCondition>(
      new LastRunStepCondition { Sequence = "self", Status = "success", Since = "11:00" }, WebOptions);

    JsonNode.Parse(json)!["type"]!.GetValue<string>().Should().Be("lastRun");
    var restored = JsonSerializer.Deserialize<SequenceStepCondition>(json, WebOptions)
      .Should().BeOfType<LastRunStepCondition>().Subject;
    restored.Sequence.Should().Be("self");
    restored.Status.Should().Be("success");
    restored.Since.Should().Be("11:00");
    restored.Within.Should().BeNull();
    restored.Negate.Should().BeFalse();
    System.Text.RegularExpressions.Regex.Matches(json, "\"type\"").Should().HaveCount(1);
  }

  [Fact]
  public void ALastRunConditionWithWithinAndNegateRoundTripsWithTheTextAsWritten() {
    var json = JsonSerializer.Serialize<SequenceStepCondition>(
      new LastRunStepCondition { Sequence = "seq-daily-train", Status = "Failure", Within = "24:00:00", Negate = true }, WebOptions);

    var restored = JsonSerializer.Deserialize<SequenceStepCondition>(json, WebOptions)
      .Should().BeOfType<LastRunStepCondition>().Subject;
    restored.Within.Should().Be("24:00:00");
    restored.Status.Should().Be("Failure");
    restored.Since.Should().BeNull();
    restored.Negate.Should().BeTrue();
  }

  [Theory]
  [InlineData("all")]
  [InlineData("any")]
  [InlineData("none")]
  public void ALastRunChildOfACompositeRoundTrips(string rule) {
    var children = new SequenceStepCondition[] {
      new LastRunStepCondition { Sequence = "self", Status = "success", Since = "11:00" },
      new LastRunStepCondition { Sequence = "other", Status = "cancelled", Within = "1.00:00:00", Negate = true }
    };
    SequenceStepCondition original = rule switch {
      "any" => new AnyStepCondition { Children = children },
      "none" => new NoneStepCondition { Children = children },
      _ => new AllStepCondition { Children = children }
    };

    var json = JsonSerializer.Serialize(original, WebOptions);
    var restored = JsonSerializer.Deserialize<SequenceStepCondition>(json, WebOptions)
      .Should().BeAssignableTo<CompositeStepCondition>().Subject;

    restored.Type.Should().Be(rule);
    var first = restored.Children[0].Should().BeOfType<LastRunStepCondition>().Subject;
    first.Since.Should().Be("11:00");
    var second = restored.Children[1].Should().BeOfType<LastRunStepCondition>().Subject;
    second.Within.Should().Be("1.00:00:00");
    second.Negate.Should().BeTrue();
  }

  private static ImageVisibleStepCondition Image(string id) => new() { ImageId = id };
}
