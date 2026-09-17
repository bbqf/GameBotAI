using GameBot.Service.Contracts.Queues;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace GameBot.Service.Swagger;

/// <summary>
/// Feature 100 (issue #179): describes a queue's roster (<c>entries</c> on the single-queue response) and the
/// fields of a roster entry. The service does not feed XML comments to Swagger, and without these descriptions
/// a consumer looking for <c>GET /api/queues/{id}/entries</c> could not tell that the roster is read here.
/// </summary>
internal sealed class QueueRosterSchemaFilter : ISchemaFilter {
  private const string EntriesDescription =
    "The queue's current roster: the entries it runs, in run order. Always an array ([] when the queue has no "
    + "entries), never null. These are the queue's own entries, which can differ from its linked template's "
    + "entries (a running queue keeps the entries it started with); a template's entries are read from "
    + "GET /api/queue-templates/{id}. Reading this field from GET /api/queues/{id} is how a queue's roster is "
    + "read; there is no GET /api/queues/{id}/entries.";

  private const string EntryIdDescription =
    "Identifies this entry within its queue; pass it to DELETE /api/queues/{id}/entries/{entryId} to remove it.";

  private const string SequenceIdDescription =
    "Id of the sequence this entry runs.";

  private const string SequenceNameDescription =
    "Current name of the referenced sequence, resolved when the response is built; null when that sequence "
    + "no longer exists.";

  private const string StaleDescription =
    "True when the referenced sequence no longer exists (sequenceName is then null).";

  public void Apply(OpenApiSchema schema, SchemaFilterContext context) {
    if (schema.Properties is null) return;

    if (context.Type == typeof(QueueDetailResponse)) {
      if (schema.Properties.TryGetValue("entries", out var entries)) {
        entries.Description = EntriesDescription;
        entries.Nullable = false;
      }
    }
    else if (context.Type == typeof(QueueEntryResponse)) {
      Describe(schema, "entryId", EntryIdDescription);
      Describe(schema, "sequenceId", SequenceIdDescription);
      Describe(schema, "sequenceName", SequenceNameDescription);
      Describe(schema, "stale", StaleDescription);
    }
  }

  private static void Describe(OpenApiSchema schema, string property, string description) {
    if (schema.Properties.TryGetValue(property, out var propertySchema)) {
      propertySchema.Description = description;
    }
  }
}
