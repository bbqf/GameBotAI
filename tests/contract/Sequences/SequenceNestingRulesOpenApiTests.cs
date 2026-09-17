using System;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace GameBot.ContractTests.Sequences;

/// <summary>
/// Feature 099 (FR-007, issue #178): an If nested in another If's branch is rejected with 400, but the rule was
/// "not discoverable from the published schema". The published sequence step schema must state the nesting rules.
/// The sentences are repeated literally rather than read from the filter, so a silent rewording fails here too.
/// </summary>
public sealed class SequenceNestingRulesOpenApiTests {
  private const string StepTypesRule =
    "stepType is one of Action (the default when omitted: a primitiveAction or command step), Loop, If or Break.";
  private const string LoopBodyRule =
    "A Loop body may contain Action, If and Break steps, but not another Loop.";
  private const string IfBranchRule =
    "An If branch (then branch in body, else branch in elseBody) may contain only Action steps, plus Break steps when the If itself sits inside a Loop body.";
  private const string NoNestedIfRule =
    "An If branch must not contain another If step, or a Loop step.";
  private const string BreakRule =
    "A Break step is only valid inside a Loop body: directly, or in an If branch whose If sits in a Loop body; a top-level Break is rejected.";
  private const string MaxDepthRule =
    "The deepest permitted nesting is Loop > If > Action or Break. A sequence that breaks these rules is rejected with 400 on create, update and patch.";

  private static WebApplicationFactory<Program> CreateFactory() {
    Environment.SetEnvironmentVariable("GAMEBOT_USE_ADB", "false");
    Environment.SetEnvironmentVariable("GAMEBOT_DYNAMIC_PORT", "true");
    Environment.SetEnvironmentVariable("GAMEBOT_AUTH_TOKEN", "test-token");
    return new WebApplicationFactory<Program>();
  }

  private static async Task<JsonElement> ReadStepSchemaAsync(string schema) {
    using var app = CreateFactory();
    var client = app.CreateClient();
    var response = await client.GetAsync(new Uri("/swagger/v1/swagger.json", UriKind.Relative)).ConfigureAwait(false);
    response.EnsureSuccessStatusCode();
    using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync().ConfigureAwait(false));
    return document.RootElement.GetProperty("components").GetProperty("schemas").GetProperty(schema).Clone();
  }

  private static string? DescriptionOf(JsonElement element)
    => element.TryGetProperty("description", out var description) ? description.GetString() : null;

  [Theory]
  [InlineData("SequenceStep")]
  [InlineData("SequenceStepContract")]
  public async Task StepSchemaStatesEveryNestingRule(string schema) {
    var step = await ReadStepSchemaAsync(schema).ConfigureAwait(false);

    DescriptionOf(step).Should().NotBeNull()
      .And.Contain(StepTypesRule)
      .And.Contain(LoopBodyRule)
      .And.Contain(IfBranchRule)
      .And.Contain(NoNestedIfRule)
      .And.Contain(BreakRule)
      .And.Contain(MaxDepthRule);
  }

  [Theory]
  [InlineData("SequenceStep")]
  [InlineData("SequenceStepContract")]
  public async Task BodyPropertyStatesLoopBodyAndThenBranchRules(string schema) {
    var step = await ReadStepSchemaAsync(schema).ConfigureAwait(false);

    DescriptionOf(step.GetProperty("properties").GetProperty("body")).Should().NotBeNull()
      .And.Contain(LoopBodyRule)
      .And.Contain(IfBranchRule)
      .And.Contain(NoNestedIfRule);
  }

  [Theory]
  [InlineData("SequenceStep")]
  [InlineData("SequenceStepContract")]
  public async Task ElseBodyPropertyStatesElseBranchRules(string schema) {
    var step = await ReadStepSchemaAsync(schema).ConfigureAwait(false);

    DescriptionOf(step.GetProperty("properties").GetProperty("elseBody")).Should().NotBeNull()
      .And.Contain(IfBranchRule)
      .And.Contain(NoNestedIfRule);
  }
}
