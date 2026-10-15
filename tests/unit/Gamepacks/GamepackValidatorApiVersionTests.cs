using System;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using GameBot.Domain.Gamepacks;
using Xunit;

#pragma warning disable CA1707, CA2007, CA1861, CA1416, CA1062

namespace GameBot.UnitTests.Gamepacks;

/// <summary>Feature 144: the check of the manifest field requiredApiMajor.</summary>
public sealed class GamepackValidatorApiVersionTests : IDisposable {
  private readonly GamepackTestKit _kit = new();

  public void Dispose() => _kit.Dispose();

  private static string PackWith(string requiredApiMajorJson) =>
    "{\"manifest\":{\"name\":\"p\",\"version\":\"1\"" + (requiredApiMajorJson.Length == 0 ? string.Empty : ",\"requiredApiMajor\":" + requiredApiMajorJson) + "}}";

  [Fact]
  public async Task AnAbsentFieldGivesNoProblem() {
    var result = await _kit.CheckAsync(PackWith(string.Empty));
    result.IsValid.Should().BeTrue();
  }

  [Fact]
  public async Task TheRunningMajorGivesNoProblem() {
    var result = await _kit.CheckAsync(PackWith(ApiVersion.Major.ToString(System.Globalization.CultureInfo.InvariantCulture)));
    result.IsValid.Should().BeTrue();
  }

  [Theory]
  [InlineData("0")]
  [InlineData("-1")]
  public async Task AValueBelowOneIsAStructureProblem(string value) {
    var result = await _kit.CheckAsync(PackWith(value));
    var problem = result.Problems.Should().ContainSingle().Subject;
    problem.Category.Should().Be("structure");
    problem.ObjectType.Should().Be("manifest");
    problem.Message.Should().Be("The required API major version must be a whole number of 1 or more.");
  }

  [Fact]
  public async Task ADifferentMajorIsAStructureProblemThatNamesBothVersions() {
    var other = ApiVersion.Major + 1;
    var result = await _kit.CheckAsync(PackWith(other.ToString(System.Globalization.CultureInfo.InvariantCulture)));
    var problem = result.Problems.Should().ContainSingle().Subject;
    problem.Category.Should().Be("structure");
    problem.ObjectType.Should().Be("manifest");
    problem.Name.Should().Be("p");
    problem.Message.Should().Be($"The gamepack needs API major version {other}. This platform runs API major version {ApiVersion.Major}.");
  }

  [Fact]
  public async Task TheCheckCanBeSkipped() {
    var parsed = GamepackFileSerializer.Parse(PackWith("99"));
    var result = await _kit.Validator.ValidateAsync(parsed.File!, parsed.Problems, checkApiVersion: false);
    result.IsValid.Should().BeTrue();
  }

  [Theory]
  [InlineData("1.5")]
  [InlineData("\"x\"")]
  public void ANonIntegerValueGivesTheWrongShapeProblemFromTheParser(string value) {
    var parsed = GamepackFileSerializer.Parse(PackWith(value));
    parsed.InvalidJson.Should().BeFalse();
    parsed.File!.Manifest.Should().BeNull();
    var problem = parsed.Problems.Should().ContainSingle().Subject;
    problem.Category.Should().Be("structure");
    problem.ObjectType.Should().Be("manifest");
    problem.Message.Should().Be("The manifest has a wrong shape.");
  }
}
