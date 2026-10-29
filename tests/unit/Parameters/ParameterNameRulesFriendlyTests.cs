#pragma warning disable CA1861, CA1825 // Test data arrays are small and used one time.
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using GameBot.Domain.Parameters;
using Xunit;

namespace GameBot.UnitTests.Parameters;

/// <summary>Feature 168: declaration rules for the friendly parameter members (data-model.md).</summary>
public sealed class ParameterNameRulesFriendlyTests {
  private static IReadOnlyList<string> Validate(ParameterDeclaration declaration) =>
      ParameterNameRules.ValidateDeclarations(new[] { declaration });

  private static ParameterDeclaration Choice(params (string Label, string Value)[] options) => new() {
    Name = "mode",
    Kind = ParameterInputKind.Choice,
    Options = options.Select(o => new ParameterOption { Label = o.Label, Value = o.Value }).ToList()
  };

  private static void ShouldHaveHint(IReadOnlyList<string> errors) {
    errors.Should().NotBeEmpty();
    errors.Should().OnlyContain(e => e.Contains(". ", System.StringComparison.Ordinal),
        "each message names the problem and gives one fix");
  }

  [Fact]
  public void DeclarationWithoutNewMembersStaysValid() {
    Validate(new ParameterDeclaration { Name = "a" }).Should().BeEmpty();
    Validate(new ParameterDeclaration { Name = "n", Type = ParameterValueType.Number, Default = "3" }).Should().BeEmpty();
  }

  [Fact]
  public void ValidChoiceNumberImageAreAccepted() {
    Validate(Choice(("Fast", "f"), ("Slow", "s"))).Should().BeEmpty();
    Validate(Choice(("Only", "o"))).Should().BeEmpty();
    Validate(new ParameterDeclaration {
      Name = "n", Type = ParameterValueType.Number, Kind = ParameterInputKind.Number,
      Min = 1, Max = 9, Unit = "ms", Pattern = "emulator-{value}", Default = "5"
    }).Should().BeEmpty();
    Validate(new ParameterDeclaration {
      Name = "img", Kind = ParameterInputKind.Image, AllowedImages = new[] { "a", "b" }
    }).Should().BeEmpty();
  }

  [Fact]
  public void MemberThatDoesNotFitTheKindIsRefused() {
    var onNumber = Validate(new ParameterDeclaration {
      Name = "n", Type = ParameterValueType.Number, Options = new[] { new ParameterOption { Label = "a", Value = "1" } }
    });
    ShouldHaveHint(onNumber);

    ShouldHaveHint(Validate(new ParameterDeclaration { Name = "t", Min = 1 }));
    ShouldHaveHint(Validate(new ParameterDeclaration { Name = "t", AllowedImages = new[] { "a" } }));
    ShouldHaveHint(Validate(new ParameterDeclaration { Name = "t", Unit = "ms" }));
  }

  [Fact]
  public void UnknownKindIsRefused() {
    ShouldHaveHint(Validate(new ParameterDeclaration { Name = "t", Kind = (ParameterInputKind)(-1) }));
  }

  [Fact]
  public void KindTextWithTypeNumberIsRefused() {
    var errors = Validate(new ParameterDeclaration {
      Name = "n", Type = ParameterValueType.Number, Kind = ParameterInputKind.Text
    });
    ShouldHaveHint(errors);
  }

  [Fact]
  public void KindImageWithTypeNumberIsRefused() {
    ShouldHaveHint(Validate(new ParameterDeclaration {
      Name = "n", Type = ParameterValueType.Number, Kind = ParameterInputKind.Image, AllowedImages = new[] { "a" }
    }));
  }

  [Fact]
  public void ChoiceNeedsBetweenOneAndFiftyOptions() {
    ShouldHaveHint(Validate(new ParameterDeclaration { Name = "c", Kind = ParameterInputKind.Choice }));
    ShouldHaveHint(Validate(Choice()));

    var fifty = Enumerable.Range(1, 50).Select(i => ($"L{i}", $"v{i}")).ToArray();
    Validate(Choice(fifty)).Should().BeEmpty();

    var fiftyOne = Enumerable.Range(1, 51).Select(i => ($"L{i}", $"v{i}")).ToArray();
    ShouldHaveHint(Validate(Choice(fiftyOne)));
  }

  [Fact]
  public void DuplicateOptionLabelWithoutCaseAndDuplicateValueAreRefused() {
    ShouldHaveHint(Validate(Choice(("Fast", "a"), ("fast", "b"))));
    ShouldHaveHint(Validate(Choice(("Fast", "a"), ("Slow", "a"))));
  }

  [Fact]
  public void EmptyOptionLabelOrValueIsRefused() {
    ShouldHaveHint(Validate(Choice(("", "a"))));
    ShouldHaveHint(Validate(Choice(("A", ""))));
  }

  [Fact]
  public void NumberChoiceNeedsWholeNumberValues() {
    var errors = Validate(new ParameterDeclaration {
      Name = "c", Type = ParameterValueType.Number, Kind = ParameterInputKind.Choice,
      Options = new[] { new ParameterOption { Label = "One", Value = "1" }, new ParameterOption { Label = "Two", Value = "x" } }
    });
    ShouldHaveHint(errors);

    Validate(new ParameterDeclaration {
      Name = "c", Type = ParameterValueType.Number, Kind = ParameterInputKind.Choice,
      Options = new[] { new ParameterOption { Label = "One", Value = "1" } }
    }).Should().BeEmpty();
  }

  [Fact]
  public void MinAboveMaxIsRefused() {
    ShouldHaveHint(Validate(new ParameterDeclaration {
      Name = "n", Type = ParameterValueType.Number, Min = 9, Max = 1
    }));
  }

  [Theory]
  [InlineData("no marker")]
  [InlineData("{value}-{value}")]
  public void PatternNeedsExactlyOneMarker(string pattern) {
    ShouldHaveHint(Validate(new ParameterDeclaration {
      Name = "n", Type = ParameterValueType.Number, Pattern = pattern
    }));
  }

  [Fact]
  public void PatternOnTextParameterIsRefused() {
    ShouldHaveHint(Validate(new ParameterDeclaration { Name = "t", Pattern = "a-{value}" }));
  }

  [Fact]
  public void AllowedImagesNeedBetweenOneAndFiftyUniqueIds() {
    ShouldHaveHint(Validate(new ParameterDeclaration {
      Name = "i", Kind = ParameterInputKind.Image, AllowedImages = new string[0]
    }));
    ShouldHaveHint(Validate(new ParameterDeclaration { Name = "i", Kind = ParameterInputKind.Image }));
    ShouldHaveHint(Validate(new ParameterDeclaration {
      Name = "i", Kind = ParameterInputKind.Image, AllowedImages = Enumerable.Range(1, 51).Select(i => $"img{i}").ToList()
    }));
    ShouldHaveHint(Validate(new ParameterDeclaration {
      Name = "i", Kind = ParameterInputKind.Image, AllowedImages = new[] { "a", "a" }
    }));
  }

  [Fact]
  public void DefaultMustFitTheKind() {
    ShouldHaveHint(Validate(new ParameterDeclaration {
      Name = "c", Kind = ParameterInputKind.Choice, Default = "zzz",
      Options = new[] { new ParameterOption { Label = "A", Value = "a" } }
    }));
    ShouldHaveHint(Validate(new ParameterDeclaration {
      Name = "n", Type = ParameterValueType.Number, Min = 1, Max = 5, Default = "9"
    }));
    ShouldHaveHint(Validate(new ParameterDeclaration {
      Name = "i", Kind = ParameterInputKind.Image, AllowedImages = new[] { "a" }, Default = "b"
    }));
  }

  [Fact]
  public void LabelAndHelpTextLengthAreLimited() {
    Validate(new ParameterDeclaration { Name = "t", Label = new string('a', 80), HelpText = new string('b', 300) })
        .Should().BeEmpty();
    ShouldHaveHint(Validate(new ParameterDeclaration { Name = "t", Label = new string('a', 81) }));
    ShouldHaveHint(Validate(new ParameterDeclaration { Name = "t", Label = " " }));
    ShouldHaveHint(Validate(new ParameterDeclaration { Name = "t", HelpText = new string('b', 301) }));
    ShouldHaveHint(Validate(new ParameterDeclaration {
      Name = "n", Type = ParameterValueType.Number, Unit = new string('u', 21)
    }));
  }

  [Fact]
  public void BracesAreAllowedInsideLongerTextOfOptionValueAndPattern() {
    Validate(Choice(("A", "prefix-{{x}}-suffix"))).Should().BeEmpty();
    Validate(new ParameterDeclaration {
      Name = "n", Type = ParameterValueType.Number, Pattern = "{{x}}-{value}"
    }).Should().BeEmpty();
  }

  [Fact]
  public void OptionValueThatIsExactlyOnePlaceholderIsRefused() {
    ShouldHaveHint(Validate(Choice(("A", "{{a}}"))));
  }
}
