using System.Collections.Generic;
using FluentAssertions;
using GameBot.Domain.Parameters;
using Xunit;

namespace GameBot.UnitTests.Parameters;

/// <summary>Feature 078: name rules and declaration-list validation (FR-003).</summary>
public sealed class ParameterNameRulesTests {
  [Theory]
  [InlineData("adbSerial")]
  [InlineData("_private")]
  [InlineData("waitMs2")]
  public void ValidIdentifiersAreAccepted(string name) {
    ParameterNameRules.ValidateName(name).Should().BeNull();
  }

  [Theory]
  [InlineData("")]
  [InlineData("   ")]
  [InlineData(null)]
  public void EmptyNamesAreRejected(string? name) {
    ParameterNameRules.ValidateName(name).Should().Contain("must not be empty");
  }

  [Theory]
  [InlineData("2fast")]
  [InlineData("has-dash")]
  [InlineData("has space")]
  [InlineData("has.dot")]
  public void MalformedIdentifiersAreRejected(string name) {
    ParameterNameRules.ValidateName(name).Should().Contain("not a valid identifier");
  }

  [Fact]
  public void IterationIsReserved() {
    ParameterNameRules.ValidateName("iteration").Should().Contain("reserved");
  }

  [Theory]
  [InlineData("queue")]
  [InlineData("queue.emulatorSerial")]
  [InlineData("queue.anythingElse")]
  public void QueueNamespaceIsReserved(string name) {
    ParameterNameRules.ValidateName(name).Should().Contain("reserved 'queue' namespace");
  }

  [Fact]
  public void DuplicateNamesAreRejected() {
    var errors = ParameterNameRules.ValidateDeclarations(new[] {
      new ParameterDeclaration { Name = "adbSerial" },
      new ParameterDeclaration { Name = "adbSerial" }
    });

    errors.Should().ContainSingle(e => e.Contains("duplicate parameter name 'adbSerial'"));
  }

  [Fact]
  public void NamesDifferingOnlyByCaseAreRejectedAsDuplicates() {
    // Names resolve case-sensitively, so allowing both would let two declarations look distinct
    // while a single reference could only ever reach one of them.
    var errors = ParameterNameRules.ValidateDeclarations(new[] {
      new ParameterDeclaration { Name = "adbSerial" },
      new ParameterDeclaration { Name = "adbserial" }
    });

    errors.Should().ContainSingle(e => e.Contains("duplicate parameter name"));
  }

  [Fact]
  public void NumericDefaultThatIsNotAWholeNumberIsRejected() {
    var errors = ParameterNameRules.ValidateDeclarations(new[] {
      new ParameterDeclaration { Name = "waitMs", Type = ParameterValueType.Number, Default = "soon" }
    });

    errors.Should().ContainSingle(e => e.Contains("is not a whole number"));
  }

  [Fact]
  public void NumericDefaultThatIsAWholeNumberIsAccepted() {
    ParameterNameRules.ValidateDeclarations(new[] {
      new ParameterDeclaration { Name = "waitMs", Type = ParameterValueType.Number, Default = "5000" }
    }).Should().BeEmpty();
  }

  [Fact]
  public void TextDefaultIsNotNumberChecked() {
    ParameterNameRules.ValidateDeclarations(new[] {
      new ParameterDeclaration { Name = "serial", Default = "emulator-5558" }
    }).Should().BeEmpty();
  }

  [Fact]
  public void NullDeclarationListIsValid() {
    ParameterNameRules.ValidateDeclarations(null).Should().BeEmpty();
  }

  [Fact]
  public void BuiltInCatalogueCoversTheFourQueueDerivedValues() {
    ParameterNameRules.BuiltIns.Should().HaveCount(4);
    ParameterNameRules.IsBuiltIn(ParameterNameRules.QueueEmulatorSerial).Should().BeTrue();
    ParameterNameRules.IsBuiltIn(ParameterNameRules.QueueInstanceName).Should().BeTrue();
    ParameterNameRules.IsBuiltIn(ParameterNameRules.QueueInstanceIndex).Should().BeTrue();
    ParameterNameRules.IsBuiltIn(ParameterNameRules.QueueGameId).Should().BeTrue();
    ParameterNameRules.IsBuiltIn("queue.nope").Should().BeFalse();
  }

  [Fact]
  public void EveryBuiltInCarriesADescriptionForThePicker() {
    ParameterNameRules.BuiltIns.Should().OnlyContain(b => !string.IsNullOrWhiteSpace(b.Description));
  }
}
