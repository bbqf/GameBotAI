using System.Collections.Generic;
using FluentAssertions;
using GameBot.Domain.Commands;
using GameBot.Domain.Utils;
using Xunit;

namespace GameBot.UnitTests.Sequences;

public sealed class TemplateSubstitutorTests {
  // ──────────────────────────────────────────────────────────────────────── //
  // Substitute(string, ...)
  // ──────────────────────────────────────────────────────────────────────── //

  // Feature 078: the pattern was widened from \w+ to dotted identifiers so the reserved queue
  // built-ins parse. It is a strict superset, so every pre-existing placeholder still matches.

  [Fact]
  public void SubstituteReplacesDottedBuiltInName() {
    var result = TemplateSubstitutor.Substitute(
        "{{queue.emulatorSerial}}",
        new Dictionary<string, string> { ["queue.emulatorSerial"] = "emulator-5558" });

    result.Should().Be("emulator-5558");
  }

  [Fact]
  public void ExtractKeysReturnsDistinctNamesInOrderOfAppearance() {
    TemplateSubstitutor.ExtractKeys("{{b}}-{{a}}-{{b}}")
        .Should().Equal("b", "a");
  }

  [Fact]
  public void ExtractKeysOnPlaceholderFreeTextIsEmpty() {
    TemplateSubstitutor.ExtractKeys("emulator-5558").Should().BeEmpty();
    TemplateSubstitutor.ExtractKeys(null).Should().BeEmpty();
  }

  [Fact]
  public void ContainsPlaceholderDetectsBothPlainAndDottedNames() {
    TemplateSubstitutor.ContainsPlaceholder("x{{iteration}}").Should().BeTrue();
    TemplateSubstitutor.ContainsPlaceholder("{{queue.gameId}}").Should().BeTrue();
    TemplateSubstitutor.ContainsPlaceholder("plain").Should().BeFalse();
    TemplateSubstitutor.ContainsPlaceholder(null).Should().BeFalse();
  }

  [Fact]
  public void TrySubstituteSucceedsWhenEveryKeyResolves() {
    var ok = TemplateSubstitutor.TrySubstitute(
        "{{a}}/{{b}}",
        new Dictionary<string, string> { ["a"] = "1", ["b"] = "2" },
        out var result,
        out var unresolved);

    ok.Should().BeTrue();
    result.Should().Be("1/2");
    unresolved.Should().BeEmpty();
  }

  [Fact]
  public void TrySubstituteReportsUnresolvedKeysInsteadOfPassingThemThrough() {
    var ok = TemplateSubstitutor.TrySubstitute(
        "{{known}}/{{missing}}",
        new Dictionary<string, string> { ["known"] = "1" },
        out _,
        out var unresolved);

    ok.Should().BeFalse();
    unresolved.Should().Equal("missing");
  }

  [Fact]
  public void TrySubstituteOnPlaceholderFreeTextSucceeds() {
    TemplateSubstitutor.TrySubstitute("plain", new Dictionary<string, string>(), out var result, out var unresolved)
        .Should().BeTrue();
    result.Should().Be("plain");
    unresolved.Should().BeEmpty();
  }

  [Fact]
  public void SubstituteReplacesKnownPlaceholder() {
    var result = TemplateSubstitutor.Substitute(
        "item-{{iteration}}",
        new Dictionary<string, string> { ["iteration"] = "3" });

    result.Should().Be("item-3");
  }

  [Fact]
  public void SubstituteLeavesUnknownKeyAsIs() {
    var result = TemplateSubstitutor.Substitute(
        "item-{{unknown}}",
        new Dictionary<string, string> { ["iteration"] = "3" });

    result.Should().Be("item-{{unknown}}");
  }

  [Fact]
  public void SubstituteReplacesMultiplePlaceholdersInOneString() {
    var result = TemplateSubstitutor.Substitute(
        "row={{row}}, col={{col}}",
        new Dictionary<string, string> { ["row"] = "2", ["col"] = "5" });

    result.Should().Be("row=2, col=5");
  }

  [Fact]
  public void SubstituteEmptyContextReturnsTemplateUnchanged() {
    const string template = "prefix-{{iteration}}-suffix";
    var result = TemplateSubstitutor.Substitute(template, new Dictionary<string, string>());

    result.Should().Be(template);
  }

  [Fact]
  public void SubstituteNoPlaceholderReturnsTextUnchanged() {
    const string text = "no placeholders here";
    var result = TemplateSubstitutor.Substitute(
        text,
        new Dictionary<string, string> { ["iteration"] = "1" });

    result.Should().Be(text);
  }

  // ──────────────────────────────────────────────────────────────────────── //
  // SubstitutePayload(...)
  // ──────────────────────────────────────────────────────────────────────── //

  [Fact]
  public void SubstitutePayloadReplacesStringParameterPlaceholder() {
    var payload = new SequenceActionPayload { Type = "tap" };
    payload.Parameters["label"] = "step-{{iteration}}";

    var result = TemplateSubstitutor.SubstitutePayload(
        payload,
        new Dictionary<string, string> { ["iteration"] = "2" });

    result.Parameters["label"].Should().Be("step-2");
  }

  [Fact]
  public void SubstitutePayloadLeavesNonStringValueUntouched() {
    var payload = new SequenceActionPayload { Type = "tap" };
    payload.Parameters["x"] = 100;
    payload.Parameters["y"] = 200;

    var result = TemplateSubstitutor.SubstitutePayload(
        payload,
        new Dictionary<string, string> { ["x"] = "999" });

    result.Parameters["x"].Should().Be(100);
    result.Parameters["y"].Should().Be(200);
  }

  [Fact]
  public void SubstitutePayloadPreservesPayloadType() {
    var payload = new SequenceActionPayload { Type = "swipe" };
    payload.Parameters["direction"] = "right";

    var result = TemplateSubstitutor.SubstitutePayload(
        payload,
        new Dictionary<string, string>());

    result.Type.Should().Be("swipe");
  }

  [Fact]
  public void SubstitutePayloadReplacesMultipleStringParameters() {
    var payload = new SequenceActionPayload { Type = "label" };
    payload.Parameters["first"] = "{{a}}";
    payload.Parameters["second"] = "{{b}}";
    payload.Parameters["third"] = 42; // non-string

    var ctx = new Dictionary<string, string> { ["a"] = "alpha", ["b"] = "beta" };
    var result = TemplateSubstitutor.SubstitutePayload(payload, ctx);

    result.Parameters["first"].Should().Be("alpha");
    result.Parameters["second"].Should().Be("beta");
    result.Parameters["third"].Should().Be(42);
  }
}
