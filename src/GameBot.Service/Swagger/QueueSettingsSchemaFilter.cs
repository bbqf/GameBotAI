using System.Collections.Generic;
using GameBot.Domain.QueueSettings;
using GameBot.Service.Endpoints;
using Microsoft.OpenApi.Any;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace GameBot.Service.Swagger;

/// <summary>
/// Feature 145: describes the types of the queue settings export and import. The service does not give XML comments to
/// Swagger, so the descriptions come from this filter.
/// </summary>
internal sealed class QueueSettingsSchemaFilter : ISchemaFilter {
  internal const string DocumentDescription =
    "The queue settings document. formatVersion is 1. The lists are queues, queueTemplates, sequences, commands, and references. "
    + "The document has no id and no time stamp of the instance. All links use names. A sequence and a command have the JSON "
    + "of the stored object without id and time stamps. The lists sequences and commands hold only objects that no gamepack owns.";

  internal const string ImportBodyDescription =
    "A queue settings document (see SettingsDocument). The request can have at most 20 MB. The import checks the whole "
    + "document before it writes. A wrong format version, a body that is not a JSON object, an empty name, or a repeated name "
    + "(case is ignored) gives status 400 and changes nothing.";

  internal const string ReportDescription =
    "The result of an import. items has one entry for each object of the document. unresolvedReferences lists each link whose "
    + "target is missing. When a store write fails, the status is 500: items holds what the import applied, failed holds the "
    + "item that failed, and notApplied holds the items that it did not try. Send the same document again to finish.";

  private static readonly Dictionary<string, string> ReferenceFields = new() {
    ["type"] = "sequence, command, image, queueTemplate, or game.",
    ["name"] = "The name of the object. For an image, the image id.",
    ["gamepack"] = "The name of the gamepack that owns the object, or null.",
    ["version"] = "The version of the gamepack that owns the object, or null.",
    ["usedBy"] = "The user of the object as type:name, for example queueTemplate:Daily."
  };

  public void Apply(OpenApiSchema schema, SchemaFilterContext context) {
    if (context.Type == typeof(QueueSettingsEndpoints.QueueSettingsImportRequestBody)) {
      schema.Description = ImportBodyDescription;
      return;
    }

    if (context.Type == typeof(SettingsDocument)) {
      schema.Description = DocumentDescription;
      Describe(schema, "references", "The uses of gamepack-owned objects and of other objects that the document does not carry. The import uses it only for report text.");
      return;
    }

    if (context.Type == typeof(NameReference) || context.Type == typeof(UnresolvedReference)) {
      schema.Description = context.Type == typeof(NameReference)
        ? "A use of an object by name, with no content."
        : "A link whose target is missing on this instance.";
      foreach (var (name, description) in ReferenceFields) {
        Describe(schema, name, description);
      }

      return;
    }

    if (context.Type == typeof(ImportReport)) {
      schema.Description = ReportDescription;
      return;
    }

    if (context.Type == typeof(ImportItem)) {
      schema.Description = "The result for one object of the document.";
      Describe(schema, "outcome", "created, updated, unchanged, skipped, or failed (failed only with status 500).");
      Describe(schema, "reason", "A short reason, or null. For a skipped item the value is owned_by_gamepack.");
      Describe(schema, "restartRequired", "True for an updated queue that is not stopped. The import never stops or starts a queue.");
      if (schema.Properties.TryGetValue("outcome", out var outcome)) {
        outcome.Example = new OpenApiString("created");
      }

      return;
    }

    if (context.Type == typeof(ImportSummary)) {
      schema.Description = "The number of items for each outcome.";
      return;
    }

    if (context.Type == typeof(QueueSettingsDoc)) {
      schema.Description = "The settings of one queue. The key is name. The device fields (emulatorSerial, emulatorInstanceName, emulatorInstanceIndex) belong to one machine. The import takes them as given.";
      Describe(schema, "linkedTemplate", "The name of the linked queue template, or null.");
      Describe(schema, "linkedGame", "The name of the linked game, or null. Games are not exported.");
      Describe(schema, "entries", "The sequence names of the queue entries, in order.");
      return;
    }

    if (context.Type == typeof(QueueTemplateDoc)) {
      schema.Description = "A queue template. The key is name. The entries are in run order.";
      return;
    }

    if (context.Type == typeof(QueueTemplateEntryDoc)) {
      schema.Description = "One entry of a queue template. sequence is a sequence name. It can name a sequence that a gamepack owns.";
    }
  }

  private static void Describe(OpenApiSchema schema, string property, string description) {
    if (schema.Properties.TryGetValue(property, out var value)) {
      value.Description = description;
    }
  }
}
