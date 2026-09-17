using GameBot.Service.Models;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace GameBot.Service.Swagger;

/// <summary>
/// Feature 103 (issue #193): publishes the two rules a <c>commandOutcome</c> condition's
/// <c>stepRef</c> is subject to, the full set of <c>expectedState</c> values, and a loop's
/// <c>exitReason</c> shape.
/// <para>
/// Issue #193 was filed because the widening of the reference scope and the addition of the
/// <c>break</c>/<c>no_break</c> states were reported as delivered but never re-measured, and because
/// nothing published said what the rules actually were — so the only way to learn them was to have a
/// save rejected with 400, or to read the validator. The service does not feed XML comments to
/// Swagger, so a description has to be set here to reach the document at all.
/// </para>
/// </summary>
internal sealed class ConditionReferenceScopeSchemaFilter : ISchemaFilter {
  internal const string ResolutionRule =
    "stepRef may name any step reachable from the sequence root — root steps plus every Loop body and "
    + "every If branch (body/elseBody), recursively — not only an immediate sibling. A stepRef that "
    + "names no step in the sequence is rejected with 400 'references unknown prior step'.";

  internal const string OrderingRule =
    "The referenced step must be prior in authored (document) order: the same preorder walk used for "
    + "resolution. A reference to a step that appears later — including a step inside the referencing "
    + "If's own body, which is authored after its condition — is rejected with 400 'must reference a "
    + "prior step'.";

  internal const string CompositeRule =
    "Both rules apply wherever the condition sits: written directly as a step guard, an if condition or "
    + "a break condition, and equally when reached through an all/any/none composite, at any nesting "
    + "depth. A rejection inside a composite names the offending child by its $-rooted path, for "
    + "example $.children[1] or $.children[2].children[0].";

  internal const string LoopConditionException =
    "One documented exception: a commandOutcome written directly as a while or repeatUntil loop's own "
    + "condition is not reference-checked, because validating leaf conditions in those two slots would "
    + "newly reject sequences that save today. The same reference wrapped in a composite there IS "
    + "checked. This asymmetry is inherited from feature 088 and is deliberate.";

  internal const string ExpectedStateRule =
    "expectedState is one of success, failed, skipped, break or no_break. success/failed/skipped are the "
    + "referenced step's own result. break and no_break ask whether a referenced Break step fired or did "
    + "not, which is how a caller distinguishes those two outcomes; they are only meaningful against a "
    + "Break step. Any other value is rejected with 400.";

  internal const string RuntimeUnavailableRule =
    "A reference that is valid at save time but names a step that did not execute in a given run — an If "
    + "branch not taken, a loop body that ran zero iterations — fails the referencing step and the run "
    + "with a 'reference is not available' error. It is deliberately not softened into a silent skip, "
    + "because a guard that could not be answered is not the same as one that answered false.";

  internal const string StepRefDescription = ResolutionRule + " " + OrderingRule + " " + CompositeRule + " " + LoopConditionException;

  internal const string ConditionDescription =
    "Asks about a prior step's outcome. " + ResolutionRule + " " + OrderingRule + " " + ExpectedStateRule
    + " " + RuntimeUnavailableRule;

  internal const string LoopExitReasonDescription =
    "A Loop step's run result carries exitReason: { brokeVia, exhaustedMaxIterations }, distinguishing "
    + "the three ways a loop can stop. brokeVia is the stepId of the Break that fired — the Break's own "
    + "id, never an enclosing If's — or null if none fired. exhaustedMaxIterations is true when the loop "
    + "ran its full configured maxIterations with no Break firing, independent of exitOnMaxIterations. "
    + "The two are mutually exclusive: a Break firing on the same iteration that reaches the ceiling "
    + "reports the break, with exhaustedMaxIterations false. Both null/false means the loop finished its "
    + "body or condition normally. It is published on the POST /api/sequences/{sequenceId}/execute "
    + "response and, since feature 103, recorded on the loop step's entry in the execution log as the "
    + "brokeVia and exhaustedMaxIterations attributes beside iterations. A loop that failed early carries "
    + "no exit reason.";

  public void Apply(OpenApiSchema schema, SchemaFilterContext context) {
    if (context.Type == typeof(CommandOutcomeConditionContract)) {
      schema.Description = ConditionDescription;
      Describe(schema, "stepRef", StepRefDescription);
      Describe(schema, "expectedState", ExpectedStateRule);
      return;
    }

    // The run response is the domain result serialized directly, so it has no contract type to hang
    // this on. The execution-log entry is the registered schema a consumer reaches for when asking
    // "why did that loop stop", so the exit reason is described there.
    if (context.Type == typeof(ExecutionLogEntryDto)) {
      Describe(schema, "details", LoopExitReasonDescription);
    }
  }

  private static void Describe(OpenApiSchema schema, string property, string description) {
    if (schema.Properties is not null && schema.Properties.TryGetValue(property, out var propertySchema)) {
      propertySchema.Description = description;
    }
  }
}
