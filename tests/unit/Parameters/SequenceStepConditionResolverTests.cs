using System.Collections.ObjectModel;
using FluentAssertions;
using GameBot.Domain.Commands;
using GameBot.Domain.Parameters;
using Xunit;

namespace GameBot.UnitTests.Parameters;

/// <summary>
/// Feature 114: the condition resolver substitutes each <c>imageVisible.imageId</c> against a scope
/// before the evaluation.
/// </summary>
public sealed class SequenceStepConditionResolverTests {
  private static ParameterScope ScopeWith(params (string Name, string Value)[] values) {
    var bindings = new Collection<ParameterBinding>();
    foreach (var (name, value) in values) bindings.Add(new ParameterBinding { Name = name, Value = value });
    return ParameterScope.Empty.Child(ParameterScopeLayers.Entry, bindings, null);
  }

  private static ImageVisibleStepCondition Image(string id, double? minSimilarity = null, bool negate = false) =>
      new() { ImageId = id, MinSimilarity = minSimilarity, Negate = negate };

  [Fact]
  public void TreeWithNoPlaceholderComesBackAsTheSameInstance() {
    var condition = new AllStepCondition {
      Children = new SequenceStepCondition[] {
        Image("option-a"),
        new CommandOutcomeStepCondition { StepRef = "s1", ExpectedState = "success" }
      }
    };

    SequenceStepConditionResolver.TryResolve(condition, ScopeWith(), "condition", out var resolved, out var error, out var used)
        .Should().BeTrue();

    resolved.Should().BeSameAs(condition);
    error.Should().BeNull();
    used.Should().BeEmpty();
  }

  [Fact]
  public void LeafPlaceholderResolvesAndKeepsTheOtherMembers() {
    var condition = Image("{{novaOption}}", minSimilarity: 0.9, negate: true);

    SequenceStepConditionResolver.TryResolve(condition, ScopeWith(("novaOption", "option-b")), "condition",
            out var resolved, out var error, out var used)
        .Should().BeTrue();

    error.Should().BeNull();
    var image = resolved.Should().BeOfType<ImageVisibleStepCondition>().Subject;
    image.Should().NotBeSameAs(condition);
    image.ImageId.Should().Be("option-b");
    image.MinSimilarity.Should().Be(0.9);
    image.Negate.Should().BeTrue();
    condition.ImageId.Should().Be("{{novaOption}}", "the stored condition does not change");
    used.Should().ContainSingle(p => p.Name == "novaOption" && p.Value == "option-b"
        && p.OriginLayer == ParameterScopeLayers.Entry);
  }

  [Fact]
  public void LeafPlaceholderInSurroundingTextResolves() {
    SequenceStepConditionResolver.TryResolve(Image("nova-{{option}}"), ScopeWith(("option", "b")), "condition",
            out var resolved, out _, out _)
        .Should().BeTrue();

    resolved.Should().BeOfType<ImageVisibleStepCondition>().Which.ImageId.Should().Be("nova-b");
  }

  [Theory]
  [InlineData("all")]
  [InlineData("any")]
  [InlineData("none")]
  public void CompositeResolvesEachChildAndKeepsNegate(string rule) {
    var children = new SequenceStepCondition[] {
      Image("{{novaOption}}"),
      new CommandOutcomeStepCondition { StepRef = "s1", ExpectedState = "success" },
      Image("option-z")
    };
    CompositeStepCondition condition = rule switch {
      "all" => new AllStepCondition { Children = children, Negate = true },
      "any" => new AnyStepCondition { Children = children, Negate = true },
      _ => new NoneStepCondition { Children = children, Negate = true }
    };

    SequenceStepConditionResolver.TryResolve(condition, ScopeWith(("novaOption", "option-b")), "if.condition",
            out var resolved, out _, out var used)
        .Should().BeTrue();

    var composite = resolved.Should().BeAssignableTo<CompositeStepCondition>().Subject;
    composite.Should().NotBeSameAs(condition);
    composite.Rule.Should().Be(condition.Rule);
    composite.Negate.Should().BeTrue();
    composite.Children.Should().HaveCount(3);
    composite.Children[0].Should().BeOfType<ImageVisibleStepCondition>().Which.ImageId.Should().Be("option-b");
    composite.Children[1].Should().BeSameAs(children[1]);
    composite.Children[2].Should().BeSameAs(children[2]);
    used.Should().ContainSingle(p => p.Name == "novaOption");
  }

  [Fact]
  public void OtherLeafTypeComesBackAsTheSameInstance() {
    var condition = new LastRunStepCondition { Sequence = "self", Status = "success", Within = "1h" };

    SequenceStepConditionResolver.TryResolve(condition, ScopeWith(), "condition", out var resolved, out _, out _)
        .Should().BeTrue();

    resolved.Should().BeSameAs(condition);
  }

  [Fact]
  public void UnknownNameInACompositeChildFailsWithTheChildPath() {
    var condition = new AllStepCondition {
      Children = new SequenceStepCondition[] { Image("option-a"), Image("{{novaOption}}") }
    };

    SequenceStepConditionResolver.TryResolve(condition, ScopeWith(), "if.condition", out _, out var error, out _)
        .Should().BeFalse();

    error!.ParameterName.Should().Be("novaOption");
    error.Reason.Should().Be(ParameterResolutionReasons.Unresolved);
    error.FieldPath.Should().Be("if.condition.children[1].imageId");
  }

  [Fact]
  public void UnknownNameInALeafFailsWithThePrefixPath() {
    SequenceStepConditionResolver.TryResolve(Image("{{novaOption}}"), ScopeWith(), "breakCondition",
            out _, out var error, out _)
        .Should().BeFalse();

    error!.FieldPath.Should().Be("breakCondition.imageId");
    error.ToMessage("brk").Should().Be(
        "Step 'brk': parameter 'novaOption' used by field 'breakCondition.imageId' could not be resolved from any scope.");
  }

  [Fact]
  public void EmptyResultFailsAsUnresolved() {
    SequenceStepConditionResolver.TryResolve(Image("{{novaOption}}"), ScopeWith(("novaOption", "  ")), "condition",
            out _, out var error, out _)
        .Should().BeFalse();

    error!.Reason.Should().Be(ParameterResolutionReasons.Unresolved);
    error.FieldPath.Should().Be("condition.imageId");
  }

  // Feature 165: ocrText.pattern and ocrText.expected.

  private static readonly PixelRegion OcrRegion = new(10, 20, 300, 400);

  private static readonly string[] WordAndN = { "word", "n" };

  private static OcrTextStepCondition OcrPattern(string pattern, bool negate = false) =>
      new() { Region = OcrRegion, Pattern = pattern, Parser = "hh:mm:ss", Negate = negate };

  private static OcrTextStepCondition OcrExpected(string expected) =>
      new() { Region = OcrRegion, Expected = expected };

  private static OcrTextStepCondition ResolveOcr(SequenceStepCondition condition, ParameterScope scope, string prefix = "condition") {
    SequenceStepConditionResolver.TryResolve(condition, scope, prefix, out var resolved, out var error, out _)
        .Should().BeTrue();
    error.Should().BeNull();
    return resolved.Should().BeOfType<OcrTextStepCondition>().Subject;
  }

  private static ParameterResolutionError FailOcr(SequenceStepCondition condition, ParameterScope scope, string prefix = "condition") {
    SequenceStepConditionResolver.TryResolve(condition, scope, prefix, out var resolved, out var error, out _)
        .Should().BeFalse();
    resolved.Should().BeSameAs(condition);
    return error!;
  }

  [Fact]
  public void OcrPatternFullFieldResolves() {
    ResolveOcr(OcrPattern("{{countPattern}}"), ScopeWith(("countPattern", "(Gathering.*){3}")))
        .Pattern.Should().Be("(Gathering.*){3}");
  }

  [Fact]
  public void OcrPatternInlineTripleBraceGivesAQuantifier() {
    ResolveOcr(OcrPattern("(?s)(Gathering.*){{{marchTarget}}}"), ScopeWith(("marchTarget", "4")))
        .Pattern.Should().Be("(?s)(Gathering.*){4}");
  }

  [Fact]
  public void OcrPatternWithTwoPlaceholdersResolves() {
    var used = ResolveUsed(OcrPattern("({{word}}.*){{{n}}}"), ScopeWith(("word", "Gathering"), ("n", "2")), out var ocr);
    ocr.Pattern.Should().Be("(Gathering.*){2}");
    used.Select(p => p.Name).Should().BeEquivalentTo(WordAndN);
  }

  private static IReadOnlyList<ResolvedParameter> ResolveUsed(
      SequenceStepCondition condition, ParameterScope scope, out OcrTextStepCondition ocr) {
    SequenceStepConditionResolver.TryResolve(condition, scope, "condition", out var resolved, out _, out var used)
        .Should().BeTrue();
    ocr = resolved.Should().BeOfType<OcrTextStepCondition>().Subject;
    return used;
  }

  [Theory]
  [InlineData("(Gathering.*){3}")]
  [InlineData("(Gathering.*){2,5}")]
  [InlineData("(Gathering.*){2,}")]
  public void OcrPatternQuantifiersStayUnchanged(string quantifierPattern) {
    var condition = OcrPattern(quantifierPattern + "{{suffix}}");
    ResolveOcr(condition, ScopeWith(("suffix", "x"))).Pattern.Should().Be(quantifierPattern + "x");
  }

  [Fact]
  public void OcrPatternWithNoPlaceholderComesBackAsTheSameInstance() {
    var condition = OcrPattern("(Gathering.*){3}");
    SequenceStepConditionResolver.TryResolve(condition, ScopeWith(), "condition", out var resolved, out _, out var used)
        .Should().BeTrue();
    resolved.Should().BeSameAs(condition);
    used.Should().BeEmpty();
  }

  [Fact]
  public void CompositeWithNoOcrPlaceholderComesBackAsTheSameInstance() {
    var condition = new AnyStepCondition {
      Children = new SequenceStepCondition[] { OcrPattern("(Gathering.*){2,}"), OcrExpected("Level 5") }
    };
    SequenceStepConditionResolver.TryResolve(condition, ScopeWith(), "condition", out var resolved, out _, out _)
        .Should().BeTrue();
    resolved.Should().BeSameAs(condition);
  }

  [Theory]
  [InlineData("all")]
  [InlineData("any")]
  [InlineData("none")]
  public void OcrLeafInACompositeResolves(string rule) {
    var children = new SequenceStepCondition[] { Image("option-a"), OcrPattern("(Gathering.*){{{n}}}") };
    CompositeStepCondition condition = rule switch {
      "all" => new AllStepCondition { Children = children },
      "any" => new AnyStepCondition { Children = children },
      _ => new NoneStepCondition { Children = children }
    };

    SequenceStepConditionResolver.TryResolve(condition, ScopeWith(("n", "2")), "condition", out var resolved, out _, out _)
        .Should().BeTrue();

    var composite = resolved.Should().BeAssignableTo<CompositeStepCondition>().Subject;
    composite.Rule.Should().Be(condition.Rule);
    composite.Children[1].Should().BeOfType<OcrTextStepCondition>().Which.Pattern.Should().Be("(Gathering.*){2}");
    children[1].Should().BeOfType<OcrTextStepCondition>().Which.Pattern.Should().Be("(Gathering.*){{{n}}}",
        "the input leaf does not change");
  }

  [Fact]
  public void OcrLeafAtDepthTwoResolves() {
    var inner = new AnyStepCondition { Children = new SequenceStepCondition[] { OcrExpected("Level {{level}}") } };
    var condition = new AllStepCondition { Children = new SequenceStepCondition[] { Image("a"), inner } };

    SequenceStepConditionResolver.TryResolve(condition, ScopeWith(("level", "5")), "condition", out var resolved, out _, out _)
        .Should().BeTrue();

    var outer = resolved.Should().BeOfType<AllStepCondition>().Subject;
    outer.Children[1].Should().BeOfType<AnyStepCondition>().Which.Children[0]
        .Should().BeOfType<OcrTextStepCondition>().Which.Expected.Should().Be("Level 5");
    inner.Children[0].Should().BeOfType<OcrTextStepCondition>().Which.Expected.Should().Be("Level {{level}}");
  }

  [Fact]
  public void OcrPatternValueGoesInWithNoEscape() {
    ResolveOcr(OcrPattern("^{{prefix}}$"), ScopeWith(("prefix", "a.b*c")))
        .Pattern.Should().Be("^a.b*c$");
  }

  [Fact]
  public void OcrRegionParserAndNegateAreCopied() {
    var condition = OcrPattern("{{p}}", negate: true);
    var ocr = ResolveOcr(condition, ScopeWith(("p", "abc")));
    ocr.Should().NotBeSameAs(condition);
    ocr.Region.Should().BeSameAs(condition.Region);
    ocr.Parser.Should().Be("hh:mm:ss");
    ocr.Negate.Should().BeTrue();
    ocr.Expected.Should().BeNull();
  }

  [Fact]
  public void OcrInputConditionKeepsItsPlaceholderText() {
    var condition = OcrPattern("(?s)(Gathering.*){{{marchTarget}}}");
    ResolveOcr(condition, ScopeWith(("marchTarget", "4")));
    condition.Pattern.Should().Be("(?s)(Gathering.*){{{marchTarget}}}");
  }

  [Fact]
  public void OcrPatternUnknownNameFailsAsUnresolved() {
    var error = FailOcr(OcrPattern("(?s)(Gathering.*){{{marchTarget}}}"), ScopeWith());
    error.Reason.Should().Be(ParameterResolutionReasons.Unresolved);
    error.ParameterName.Should().Be("marchTarget");
    error.FieldPath.Should().Be("condition.pattern");
  }

  [Fact]
  public void OcrPatternDoubleBraceDigitsIsAPlaceholder() {
    var error = FailOcr(OcrPattern("(Gathering.*){{3}}"), ScopeWith());
    error.Reason.Should().Be(ParameterResolutionReasons.Unresolved);
    error.ParameterName.Should().Be("3");
    error.FieldPath.Should().Be("condition.pattern");
  }

  [Fact]
  public void OcrPatternWhiteSpaceFullResultFailsAsUnresolved() {
    var error = FailOcr(OcrPattern("{{countPattern}}"), ScopeWith(("countPattern", " ")));
    error.Reason.Should().Be(ParameterResolutionReasons.Unresolved);
    error.FieldPath.Should().Be("condition.pattern");
  }

  [Fact]
  public void OcrPatternEmptyValueInsideALongerPatternDoesNotFail() {
    ResolveOcr(OcrPattern("Gathering{{suffix}}"), ScopeWith(("suffix", string.Empty)))
        .Pattern.Should().Be("Gathering");
  }

  [Fact]
  public void OcrPatternInvalidResultFailsWithInvalidPattern() {
    var error = FailOcr(OcrPattern("{{countPattern}}"), ScopeWith(("countPattern", "(")));
    error.Reason.Should().Be(ParameterResolutionReasons.InvalidPattern);
    error.ParameterName.Should().Be("countPattern");
    error.FieldPath.Should().Be("condition.pattern");
    error.OffendingValue.Should().Be("(");
    error.ToMessage("s3").Should().Be(
        "Step 's3': field 'condition.pattern' resolved to the pattern '(', which is not a valid regular expression. "
        + "Change the value of 'countPattern' or the pattern.");
  }

  [Fact]
  public void OcrExpectedFullFieldResolves() {
    ResolveOcr(OcrExpected("{{accountName}}"), ScopeWith(("accountName", "Alpha")))
        .Expected.Should().Be("Alpha");
  }

  [Fact]
  public void OcrExpectedInlineFieldResolves() {
    var ocr = ResolveOcr(OcrExpected("Level {{level}}"), ScopeWith(("level", "5")));
    ocr.Expected.Should().Be("Level 5");
    ocr.Pattern.Should().BeNull();
  }

  [Fact]
  public void OcrExpectedEmptyValueInsideLongerTextDoesNotFail() {
    ResolveOcr(OcrExpected("Level {{suffix}}"), ScopeWith(("suffix", string.Empty)))
        .Expected.Should().Be("Level ");
  }

  [Fact]
  public void OcrExpectedWhiteSpaceFullResultFailsAsUnresolved() {
    var error = FailOcr(OcrExpected("{{accountName}}"), ScopeWith(("accountName", " ")));
    error.Reason.Should().Be(ParameterResolutionReasons.Unresolved);
    error.FieldPath.Should().Be("condition.expected");
  }

  [Fact]
  public void OcrExpectedUnknownNameInACompositeFailsWithTheChildPath() {
    var condition = new AllStepCondition { Children = new SequenceStepCondition[] { OcrExpected("{{accountName}}") } };
    var error = FailOcr(condition, ScopeWith());
    error.Reason.Should().Be(ParameterResolutionReasons.Unresolved);
    error.FieldPath.Should().Be("condition.children[0].expected");
  }

  [Fact]
  public void OcrExpectedIsNotCompiledAsARegularExpression() {
    ResolveOcr(OcrExpected("{{accountName}}"), ScopeWith(("accountName", "(")))
        .Expected.Should().Be("(");
  }

  [Fact]
  public void OcrExpectedInputConditionKeepsItsPlaceholderText() {
    var condition = OcrExpected("Level {{level}}");
    ResolveOcr(condition, ScopeWith(("level", "5")));
    condition.Expected.Should().Be("Level {{level}}");
  }
}
