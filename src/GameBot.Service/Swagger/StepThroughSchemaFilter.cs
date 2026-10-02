using GameBot.Domain.Logging;
using GameBot.Service.Models;
using GameBot.Service.Services.StepThrough;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace GameBot.Service.Swagger;

/// <summary>
/// Feature 127: describes the types of the step-through API and the <c>origin</c> field of the execution
/// log. The service does not feed XML comments to Swagger, so this filter holds the descriptions.
/// </summary>
internal sealed class StepThroughSchemaFilter : ISchemaFilter {
  internal const string OriginDescription =
    "Where the run came from. 'step-through': a step that the author ran in a step-through of a saved "
    + "sequence. Null for an ordinary run and for entries that were written before this field existed. "
    + "Use the origin query parameter on the list endpoint to filter by it.";

  public void Apply(OpenApiSchema schema, SchemaFilterContext context) {
    if (context.Type == typeof(ExecutionLogEntryDto)) {
      Describe(schema, "origin", OriginDescription);
    }
    else if (context.Type == typeof(StartStepThroughRequest)) {
      schema.Description = "Starts a step-through of a saved sequence. The call does not run a step.";
      Describe(schema, "sequenceId", "The id of a saved sequence.");
      Describe(schema, "gameSessionId", "The game session that receives the input. One step-through can use one game session at a time.");
      Describe(schema, "startPath", "The path of the first step. Optional. A loop or an if step cannot be a first step. Steps that were skipped have no outcome.");
      Describe(schema, "parameterValues", "Values for the parameters that the sequence declares. Optional.");
    }
    else if (context.Type == typeof(SelectStepRequest)) {
      schema.Description = "Sets the next step by hand.";
      Describe(schema, "path", "The path of a step from the step list, for example '1/body/0'. A loop or an if step cannot be selected.");
    }
    else if (context.Type == typeof(SetValuesRequest)) {
      schema.Description = "Changes the values of a step-through while no step runs. The history and the cursor stay.";
      Describe(schema, "parameterValues", "Replaces the values of the sequence parameters.");
      Describe(schema, "outcomes", "Sets step outcomes that conditions read. The key is a step id. An empty value makes the outcome 'not set'.");
    }
    else if (context.Type == typeof(StepNodeDto)) {
      schema.Description = "One row of the step list. A loop or an if step is a header row and is not selectable.";
      Describe(schema, "path", "The path of the step, for example '1/body/0' or '3/else/1'.");
      Describe(schema, "depth", "0 for a top-level step. The step list indents a row by its depth.");
      Describe(schema, "container", "True for a loop or an if step.");
      Describe(schema, "selectable", "False for a loop or an if step.");
      Describe(schema, "branch", "The branch of the parent that holds the step: 'body' or 'else'. Null at the top level.");
    }
    else if (context.Type == typeof(HistoryEntryDto)) {
      schema.Description = "One entry in the history of a step-through. The history has at most 1000 entries. The oldest entry drops first.";
      Describe(schema, "kind", "'step': one step ran. 'enter': the decision of a loop or an if step. 'exit': a loop ended.");
      Describe(schema, "iteration", "The iteration number for a step inside a loop.");
      Describe(schema, "status", "One of Succeeded, Failed, Skipped, Cancelled. The same words as the execution log.");
      Describe(schema, "effects", "The effects that the step-through previewed and did not apply, for example 'would reschedule at 14:30'.");
      Describe(schema, "notes", "Notes about the step, for example 'lastRun is not evaluated' or 'sequence would end here'.");
      Describe(schema, "executionLogId", "The id of the execution log entry of the step run.");
    }
    else if (context.Type == typeof(StepThroughQueueDto)) {
      schema.Description = "The queue that owns the device of the game session. A step-through refuses a step while the queue runs and is not paused.";
      Describe(schema, "running", "True while the queue runs and is not paused.");
      Describe(schema, "firingActive", "True while a firing of the queue runs now. The queue cannot be paused until the firing ends.");
      Describe(schema, "pausedByStepThrough", "True when this step-through paused the queue. The queue resumes when the step-through ends.");
      Describe(schema, "alreadyPaused", "True when the queue was paused before. The step-through does not resume it.");
    }
    else if (context.Type == typeof(StepThroughStateDto)) {
      schema.Description = "The state of a step-through. A read of the state renews the lease of 90 seconds.";
      Describe(schema, "state", "'idle': the next step can run. 'running': a step runs now. 'complete': no next step exists. Select a step to run it again.");
      Describe(schema, "cursor", "The path of the next step. It can be a loop or an if step: the step-through then evaluates it when the next step runs. Null when the sequence is complete.");
      Describe(schema, "nodes", "The step list. It is stable for one version of the sequence.");
      Describe(schema, "outcomes", "The step outcomes that conditions read.");
      Describe(schema, "leaseExpiresAt", "The step-through ends at this time unless the view reads the state again.");
    }
  }

  private static void Describe(OpenApiSchema schema, string property, string description) {
    if (schema.Properties is not null && schema.Properties.TryGetValue(property, out var propertySchema)) {
      propertySchema.Description = description;
    }
  }
}
