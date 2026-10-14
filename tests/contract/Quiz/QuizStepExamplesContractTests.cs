using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using FluentAssertions;
using GameBot.Service.Swagger;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

#pragma warning disable CA2007, CA1861

namespace GameBot.ContractTests.Quiz;

/// <summary>
/// Feature 142 (R16): the <c>PayloadDescriptions</c> entry of each quiz step holds the example of
/// <c>contracts/quiz-steps.md</c>, and each example passes the save-time validation.
/// </summary>
public sealed class QuizStepExamplesContractTests {
  private static WebApplicationFactory<Program> CreateFactory() {
    Environment.SetEnvironmentVariable("GAMEBOT_USE_ADB", "false");
    Environment.SetEnvironmentVariable("GAMEBOT_DYNAMIC_PORT", "true");
    Environment.SetEnvironmentVariable("GAMEBOT_AUTH_TOKEN", "test-token");
    return new WebApplicationFactory<Program>();
  }

  [Fact]
  public void EachPayloadDescriptionHoldsItsExampleStep() {
    var lookup = PrimitiveActionSchemaFilter.PayloadDescriptions["quiz-lookup"];
    var learn = PrimitiveActionSchemaFilter.PayloadDescriptions["quiz-learn"];

    lookup.Should().Contain(PrimitiveActionSchemaFilter.QuizLookupExample);
    learn.Should().Contain(PrimitiveActionSchemaFilter.QuizLearnExample);
    PrimitiveActionSchemaFilter.PayloadDescription.Should().Contain(PrimitiveActionSchemaFilter.QuizLookupExample);
  }

  [Theory]
  [InlineData("quiz-lookup")]
  [InlineData("quiz-learn")]
  public void TheExampleEqualsTheJsonOfTheContractFile(string type) {
    var example = JsonNode.Parse(type == "quiz-lookup"
      ? PrimitiveActionSchemaFilter.QuizLookupExample
      : PrimitiveActionSchemaFilter.QuizLearnExample)!;
    var fromContract = ContractBlocks().Single(b => b!["type"]!.GetValue<string>() == type)!;

    example["type"]!.GetValue<string>().Should().Be(type);
    // The contract file names the fields "parameters" (stored form). The API request names them "payload".
    JsonNode.DeepEquals(example["payload"], fromContract["parameters"]).Should().BeTrue(
      $"the example of {type} in PayloadDescriptions must equal the example in contracts/quiz-steps.md");
  }

  [Fact]
  public async Task BothExamplesPassSaveTimeValidation() {
    var lookup = JsonNode.Parse(PrimitiveActionSchemaFilter.QuizLookupExample)!;
    var learn = JsonNode.Parse(PrimitiveActionSchemaFilter.QuizLearnExample)!;
    using var app = CreateFactory();
    var client = app.CreateClient();
    client.DefaultRequestHeaders.Add("Authorization", "Bearer test-token");

    var response = await client.PostAsJsonAsync("/api/sequences", new {
      name = "quiz-examples-" + Guid.NewGuid().ToString("N"),
      version = 1,
      steps = new object[] {
        new { stepId = "lookup-1", label = "lookup", stepType = "Action", primitiveAction = JsonSerializer.Deserialize<JsonElement>(lookup.ToJsonString()) },
        new { stepId = "learn-1", label = "learn", stepType = "Action", primitiveAction = JsonSerializer.Deserialize<JsonElement>(learn.ToJsonString()) }
      }
    }).ConfigureAwait(false);

    response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync().ConfigureAwait(false));
  }

  [Fact]
  public async Task OpenApiShowsBothTypesAndTheirExamples() {
    using var app = CreateFactory();
    var client = app.CreateClient();
    client.DefaultRequestHeaders.Add("Authorization", "Bearer test-token");

    var text = await client.GetStringAsync(new Uri("/swagger/v1/swagger.json", UriKind.Relative)).ConfigureAwait(false);

    text.Should().Contain("quiz-lookup").And.Contain("quiz-learn").And.Contain("pick_N").And.Contain("lookupStepId");
    text.Should().Contain("/api/quiz/records");
  }

  private static JsonNode?[] ContractBlocks() {
    var dir = new DirectoryInfo(AppContext.BaseDirectory);
    while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "GameBot.sln"))) {
      dir = dir.Parent;
    }

    dir.Should().NotBeNull("the repository root holds GameBot.sln");
    var path = Path.Combine(dir!.FullName, "specs", "142-quiz-answer-service", "contracts", "quiz-steps.md");
    var text = File.ReadAllText(path);
    return Regex.Matches(text, "```json\\s*(\\{.*?\\})\\s*```", RegexOptions.Singleline)
      .Select(m => JsonNode.Parse(m.Groups[1].Value))
      .Where(n => n?["type"] is not null && n["parameters"] is not null)
      .ToArray();
  }
}
