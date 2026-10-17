using System;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Xunit;
using F = GameBot.UnitTests.Gamepacks.GamepackPlaceholderImageFixtures;

#pragma warning disable CA2007, CA1861, CA1707, CA1305, CA1307, CA1310

namespace GameBot.UnitTests.Gamepacks;

/// <summary>Feature 152: the reference check skips an image id that is one whole placeholder of a declared parameter.</summary>
public sealed class GamepackValidatorPlaceholderImageTests : IDisposable {
  private readonly GamepackTestKit _kit = new();

  public void Dispose() => _kit.Dispose();

  private async Task<string[]> ReferenceProblemsAsync(string json) {
    var result = await _kit.CheckAsync(json);
    return result.Problems.Where(p => p.Category == "reference").Select(p => p.Message).ToArray();
  }

  // ---- US1: no problem ----

  [Fact]
  public async Task Issue326FileHasNoProblem() {
    var result = await _kit.CheckAsync(F.Issue326Pack());
    result.Problems.Should().BeEmpty();
  }

  [Fact]
  public async Task DeclaredPlaceholderInStepConditionHasNoProblem() =>
    (await ReferenceProblemsAsync(F.Pack(F.Sequence(new[] { "p" }, F.StepWithCondition("{{p}}"))))).Should().BeEmpty();

  [Fact]
  public async Task DeclaredPlaceholderInNestedStepHasNoProblem() =>
    (await ReferenceProblemsAsync(F.Pack(F.Sequence(new[] { "p" }, F.LoopBody("{{p}}"))))).Should().BeEmpty();

  [Fact]
  public async Task DeclaredPlaceholderInBreakConditionHasNoProblem() =>
    (await ReferenceProblemsAsync(F.Pack(F.Sequence(new[] { "p" }, F.LoopBreak("{{p}}"))))).Should().BeEmpty();

  [Fact]
  public async Task DeclaredPlaceholderInIfConditionHasNoProblem() =>
    (await ReferenceProblemsAsync(F.Pack(F.Sequence(new[] { "p" }, F.IfCondition("{{p}}"))))).Should().BeEmpty();

  [Fact]
  public async Task DeclaredPlaceholderInElseBodyStepHasNoProblem() =>
    (await ReferenceProblemsAsync(F.Pack(F.Sequence(new[] { "p" }, F.IfElse("{{p}}"))))).Should().BeEmpty();

  [Fact]
  public async Task DeclaredPlaceholderWithOuterWhiteSpaceHasNoProblem() =>
    (await ReferenceProblemsAsync(F.Pack(F.Sequence(new[] { "p" }, F.StepWithCondition(" {{p}} "))))).Should().BeEmpty();

  // ---- US2: still checked ----

  [Theory]
  [InlineData("{{name}}", new string[0])]
  [InlineData("pre-{{name}}", new[] { "name" })]
  [InlineData("{{name}}-x", new[] { "name" })]
  [InlineData("missing", new[] { "name" })]
  [InlineData("{{b}}", new[] { "a" })]
  [InlineData("{{ name }}", new[] { "name" })]
  [InlineData("{{Name}}", new[] { "name" })]
  [InlineData("{{iteration}}", new string[0])]
  public async Task OtherImageIdsStillGiveAReferenceProblem(string imageId, string[] declared) {
    ArgumentNullException.ThrowIfNull(imageId);
    var problems = await ReferenceProblemsAsync(F.Pack(F.Sequence(declared, F.StepWithCondition(imageId))));
    problems.Should().ContainSingle().Which.Should().Contain(imageId.Trim());
  }

  [Fact]
  public async Task ParameterOfAnotherSequenceDoesNotCount() {
    var json = F.Pack(
      F.SequenceNamed("A", new[] { "p" }, F.StepWithCondition("{{p}}", "a1")),
      F.SequenceNamed("B", Array.Empty<string>(), F.StepWithCondition("{{p}}", "b1")));

    var result = await _kit.CheckAsync(json);

    result.Problems.Where(p => p.Category == "reference").Should().ContainSingle(p => p.Name == "B");
  }

  [Fact]
  public async Task NullParametersAreAnEmptySet() {
    var sequence = "{\"name\":\"N\",\"parameters\":null,\"steps\":[" + F.StepWithCondition("{{p}}") + "]}";
    (await ReferenceProblemsAsync(F.Pack(sequence))).Should().ContainSingle();
  }

  [Fact]
  public async Task PlainImageIdInTheFileHasNoProblem() =>
    (await ReferenceProblemsAsync(F.Pack(F.Sequence(new[] { "p" }, F.StepWithCondition("known"))))).Should().BeEmpty();

  [Fact]
  public async Task CommandImageIdWithAPlaceholderIsStillChecked() {
    var sequence = F.Sequence(new[] { "p" }, F.StepWithCondition("known"));
    var json = GamepackTestKit.Pack("demo-pack", "1.0.0",
      sequences: new object[] { System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(sequence) },
      commands: new[] { GamepackTestKit.Cmd("cmd-1", "Demo command", imageId: "{{p}}") },
      images: new[] { GamepackTestKit.Img("known") });

    var problems = await ReferenceProblemsAsync(json);

    problems.Should().ContainSingle().Which.Should().Contain("{{p}}");
  }
}
