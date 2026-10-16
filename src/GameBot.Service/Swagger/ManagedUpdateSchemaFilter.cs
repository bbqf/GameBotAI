using System.Collections.Generic;
using GameBot.Service.Endpoints.Dto;
using Microsoft.OpenApi.Any;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace GameBot.Service.Swagger;

/// <summary>
/// Feature 148: describes the types of the managed gamepack update. The service does not give XML comments to Swagger, so the
/// descriptions come from this filter.
/// </summary>
internal sealed class ManagedUpdateSchemaFilter : ISchemaFilter {
  internal const string ReportDescription =
    "The report of a managed gamepack call. result is done, planned, failed, or rejected. reason is one name from the reason list "
    + "(invalid_request, invalid_file, signature_rejected, validation_failed, template_not_found, gamepack_not_found, "
    + "no_previous_version, entry_not_listed, not_configured, catalog_error, too_large, update_in_progress, stop_timeout, "
    + "backup_failed, install_failed, booking_failed, rollback_refused, unexpected) or null. A queue in notRestartedQueues does not "
    + "change result. When restore.happened is true, the installed version and the templates equal the state before the call, unless "
    + "restore.restored is false and restore.failedObjects lists the exceptions. The platform runs one managed call at a time on the "
    + "whole instance. The stop limit is 60 seconds for each queue.";

  internal const string ErrorDescription =
    "The error body of a managed gamepack call. reason is one name from the reason list of the report.";

  internal const string CatalogRequestDescription =
    "The body of the catalog update route. name and version are required. dryRun is false by default. templates limits the call "
    + "to the named templates (case is ignored). Null or empty means all templates.";

  private static readonly Dictionary<string, string> ReportFields = new() {
    ["result"] = "done, planned (a dry run with no problem), failed (a step failed after the start), or rejected (a check ended the call before any change).",
    ["reason"] = "One name from the reason list, or null for done and planned.",
    ["detail"] = "The error text of a failed call, or null.",
    ["dryRun"] = "The value of the dryRun flag of the request.",
    ["previousVersion"] = "The version that was installed before the call, or null.",
    ["stoppedQueues"] = "The queues that the platform stopped. For a dry run: the queues that it would stop.",
    ["restartedQueues"] = "The queues that run again. The platform restarts only the queues that it stopped.",
    ["notRestartedQueues"] = "The queues that did not start, each with an error text.",
    ["backupId"] = "The id of the stored backup. Null for a dry run, for the same version, and when the update affects no queue template.",
    ["newEntries"] = "The new disabled entries (planned for a dry run).",
    ["removedSequenceEntries"] = "The template entries whose sequence the new version removed. The entries stay.",
    ["notAffectedTemplates"] = "The templates in the request list that the gamepack does not affect.",
    ["problems"] = "All problems of the check.",
    ["restore"] = "The restore part. happened is true when the flow ran at least one restore step."
  };

  public void Apply(OpenApiSchema schema, SchemaFilterContext context) {
    if (context.Type == typeof(ManagedUpdateReportDto)) {
      schema.Description = ReportDescription;
      foreach (var (name, description) in ReportFields) {
        Describe(schema, name, description);
      }

      if (schema.Properties.TryGetValue("result", out var result)) {
        result.Enum = new List<IOpenApiAny> {
          new OpenApiString("done"), new OpenApiString("planned"), new OpenApiString("failed"), new OpenApiString("rejected")
        };
      }

      return;
    }

    if (context.Type == typeof(ManagedErrorDto)) {
      schema.Description = ErrorDescription;
      return;
    }

    if (context.Type == typeof(GamepackCatalogUpdateRequest)) {
      schema.Description = CatalogRequestDescription;
      return;
    }

    if (context.Type == typeof(ManagedQueueRefDto)) {
      schema.Description = "A queue in the report. error is set only for a queue that did not start.";
      return;
    }

    if (context.Type == typeof(ManagedEntryRefDto)) {
      schema.Description = "An entry of a queue template in the report. position is the zero-based place in the template.";
      return;
    }

    if (context.Type == typeof(ManagedRestoreDto)) {
      schema.Description = "The restore part of the report. restored is true when all restore steps worked. failedObjects lists the objects that the restore could not put back.";
    }
  }

  private static void Describe(OpenApiSchema schema, string property, string description) {
    if (schema.Properties.TryGetValue(property, out var value)) {
      value.Description = description;
    }
  }
}
