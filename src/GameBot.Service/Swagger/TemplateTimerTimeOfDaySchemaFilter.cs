using GameBot.Service.Contracts.QueueTemplates;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace GameBot.Service.Swagger;

/// <summary>
/// Issue #226: states the one accepted format of a template entry's <c>timerTimeOfDay</c>, the same rule that
/// the <c>reschedule-self</c> payload uses. The service does not feed XML comments to Swagger, so the text is set here.
/// </summary>
internal sealed class TemplateTimerTimeOfDaySchemaFilter : ISchemaFilter {
  internal const string Description =
    "Time of day for a Timer entry, as HH:mm or HH:mm:ss (24-hour, service-local time). "
    + "The response writes HH:mm when the seconds are zero. Exclusive with timerRelativeOffset.";

  public void Apply(OpenApiSchema schema, SchemaFilterContext context) {
    if (schema.Properties is null) return;
    if (context.Type != typeof(TemplateEntrySaveRequest) && context.Type != typeof(QueueTemplateEntryResponse)) return;

    if (schema.Properties.TryGetValue("timerTimeOfDay", out var property)) {
      property.Description = Description;
    }
  }
}
