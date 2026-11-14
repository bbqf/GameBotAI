using System;
using GameBot.Service.Contracts.Queues;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace GameBot.Service.Swagger;

/// <summary>
/// Feature 189: describes the smart scheduling members of the queue and monitor schemas. The service does not feed XML
/// comments to Swagger.
/// </summary>
internal sealed class SmartSchedulingSchemaFilter : ISchemaFilter {
  private const string SmartSchedulingDescription =
    "When true, a Timer booking of a sequence moves to a pending firing that is not more than the window later. "
    + "Default false. A change applies to each booking made after the save.";

  private const string WindowDescription = "The window in minutes, from 1 to 120. Default 15.";

  private const string MonitorSmartSchedulingDescription =
    "The smart scheduling settings of the stored queue: { enabled, windowMinutes }. Present for a running queue and "
    + "for a stopped queue.";

  private const string RequestedAtDescription =
    "Present only for a self-reschedule firing that smart scheduling postponed. It is the requested time. expectedAt "
    + "is the booked time.";

  private static readonly Type[] QueueTypes = {
    typeof(CreateQueueRequest), typeof(UpdateQueueRequest), typeof(QueueResponse), typeof(QueueDetailResponse)
  };

  public void Apply(OpenApiSchema schema, SchemaFilterContext context) {
    if (schema.Properties is null) return;

    if (Array.IndexOf(QueueTypes, context.Type) >= 0) {
      Describe(schema, "smartScheduling", SmartSchedulingDescription);
      Describe(schema, "smartSchedulingWindowMinutes", WindowDescription);
    }
    else if (context.Type == typeof(QueueMonitorSmartSchedulingResponse)) {
      // The monitor member is a reference to this schema, so the description is on the schema itself.
      schema.Description = MonitorSmartSchedulingDescription;
      Describe(schema, "enabled", "True when the stored queue has smart scheduling on.");
      Describe(schema, "windowMinutes", WindowDescription);
    }
    else if (context.Type == typeof(QueueMonitorItemResponse)) {
      Describe(schema, "requestedAt", RequestedAtDescription);
    }
  }

  private static void Describe(OpenApiSchema schema, string property, string description) {
    if (schema.Properties.TryGetValue(property, out var propertySchema)) {
      propertySchema.Description = description;
    }
  }
}
