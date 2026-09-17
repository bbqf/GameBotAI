using GameBot.Service.Models;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace GameBot.Service.Swagger;

/// <summary>
/// Feature 099 (issue #178): publishes the step nesting rules that <c>SequenceStepValidationService</c> enforces on
/// the sequence step schema and its <c>body</c>/<c>elseBody</c> properties. The service does not feed XML comments
/// to Swagger, so without this an author learns that an If may not sit inside another If's branch only by having a
/// save rejected with 400.
/// </summary>
internal sealed class SequenceNestingRulesSchemaFilter : ISchemaFilter {
  internal const string StepTypesRule =
    "stepType is one of Action (the default when omitted: a primitiveAction or command step), Loop, If or Break.";

  internal const string LoopBodyRule =
    "A Loop body may contain Action, If and Break steps, but not another Loop.";

  internal const string IfBranchRule =
    "An If branch (then branch in body, else branch in elseBody) may contain only Action steps, plus Break steps "
    + "when the If itself sits inside a Loop body.";

  internal const string NoNestedIfRule =
    "An If branch must not contain another If step, or a Loop step.";

  internal const string BreakRule =
    "A Break step is only valid inside a Loop body: directly, or in an If branch whose If sits in a Loop body; "
    + "a top-level Break is rejected.";

  internal const string MaxDepthRule =
    "The deepest permitted nesting is Loop > If > Action or Break. A sequence that breaks these rules is rejected "
    + "with 400 on create, update and patch.";

  internal const string StepDescription =
    StepTypesRule + " " + LoopBodyRule + " " + IfBranchRule + " " + NoNestedIfRule + " " + BreakRule + " " + MaxDepthRule;

  internal const string BodyDescription =
    "For a Loop step this is the loop body. " + LoopBodyRule
    + " For an If step this is the then branch. " + IfBranchRule + " " + NoNestedIfRule;

  internal const string ElseBodyDescription =
    "For an If step this is the optional else branch (null or absent means no else branch). "
    + IfBranchRule + " " + NoNestedIfRule;

  public void Apply(OpenApiSchema schema, SchemaFilterContext context) {
    if (context.Type != typeof(SequenceStepContract)) {
      return;
    }

    schema.Description = StepDescription;
    Describe(schema, "body", BodyDescription);
    Describe(schema, "elseBody", ElseBodyDescription);
  }

  private static void Describe(OpenApiSchema schema, string property, string description) {
    if (schema.Properties is not null && schema.Properties.TryGetValue(property, out var propertySchema)) {
      propertySchema.Description = description;
    }
  }
}
