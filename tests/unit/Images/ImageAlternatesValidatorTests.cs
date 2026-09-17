#pragma warning disable CA2007, CA1861, CA1859, CA2000, CA1849
using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using GameBot.Domain.Images;
using Xunit;

namespace GameBot.UnitTests.Images {
  public sealed class ImageAlternatesValidatorTests {
    private static readonly HashSet<string> Stored = new(StringComparer.OrdinalIgnoreCase) {
      "primary", "a", "b", "c", "d", "e", "f", "g", "h", "i"
    };

    private static bool Exists(string id) => Stored.Contains(id);

    [Fact]
    public void ValidListPasses() {
      ImageAlternatesValidator.Validate("primary", new[] { "a", "b" }, Exists).IsValid.Should().BeTrue();
    }

    [Fact]
    public void EmptyListPasses() {
      ImageAlternatesValidator.Validate("primary", Array.Empty<string>(), Exists).IsValid.Should().BeTrue();
    }

    [Fact]
    public void MoreThanEightIsRefusedNamingTheLimit() {
      var result = ImageAlternatesValidator.Validate("primary", new[] { "a", "b", "c", "d", "e", "f", "g", "h", "i" }, Exists);

      result.IsValid.Should().BeFalse();
      ImageAlternatesValidator.MaxAlternates.Should().Be(8);
      result.Message.Should().Contain("8");
    }

    [Fact]
    public void EightIsAccepted() {
      ImageAlternatesValidator.Validate("primary", new[] { "a", "b", "c", "d", "e", "f", "g", "h" }, Exists)
        .IsValid.Should().BeTrue();
    }

    [Fact]
    public void SelfReferenceIsRefused() {
      var result = ImageAlternatesValidator.Validate("primary", new[] { "a", "PRIMARY" }, Exists);

      result.IsValid.Should().BeFalse();
      result.Ids.Should().Equal("PRIMARY");
    }

    [Fact]
    public void DuplicatesAreRefusedCaseInsensitively() {
      var result = ImageAlternatesValidator.Validate("primary", new[] { "a", "b", "A" }, Exists);

      result.IsValid.Should().BeFalse();
      result.Ids.Should().Equal("A");
    }

    [Fact]
    public void InvalidFormatIdsAreRefused() {
      var result = ImageAlternatesValidator.Validate("primary", new[] { "a", "bad id!" }, Exists);

      result.IsValid.Should().BeFalse();
      result.Ids.Should().Equal("bad id!");
    }

    [Fact]
    public void UnknownIdsAreRefused() {
      var result = ImageAlternatesValidator.Validate("primary", new[] { "a", "ghost", "phantom" }, Exists);

      result.IsValid.Should().BeFalse();
      result.Ids.Should().Equal("ghost", "phantom");
      result.Hint.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void NullEntriesAreRefused() {
      var result = ImageAlternatesValidator.Validate("primary", new string?[] { "a", null }!, Exists);

      result.IsValid.Should().BeFalse();
    }
  }
}
