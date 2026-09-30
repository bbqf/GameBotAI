using GameBot.Domain.Queues;
using GameBot.Service.Contracts.Queues;
using Microsoft.OpenApi.Any;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace GameBot.Service.Swagger;

/// <summary>
/// Feature 121 (FR-016, issue #261): descriptions of the <c>deviceRecovery</c> queue field. The service
/// does not give XML comments to Swagger, so this filter publishes the meaning of each member.
/// </summary>
internal sealed class QueueDeviceRecoverySchemaFilter : ISchemaFilter {
  internal const string FieldDescription =
    "Optional device recovery (feature 121). Null or absent: the queue sends the 'device not live' alert only and "
    + "never repairs the device. With action 'reboot-instance', the queue reboots its LDPlayer instance when the "
    + "device stays not live for longer than afterMs. The service starts one reboot at a time, at least "
    + "RecoveryStaggerMs apart. On PUT, an absent member clears the stored settings.";

  internal const string ActionDescription =
    "'none' (default): no recovery. 'reboot-instance': run 'ldconsole reboot --name <emulatorInstanceName>'. This "
    + "value needs emulatorInstanceName on the queue.";

  internal const string AfterMsDescription =
    "The age of one not-live episode, in milliseconds, after which the first attempt starts. Default 300000. "
    + "Minimum 60000.";

  internal const string MaxAttemptsDescription =
    "The count of attempts for one episode. Default 2. From 1 to 5. After the last failed attempt, the service "
    + "sends one 'recovery failed' alert. The queue stays Running.";

  internal const string CooldownMsDescription =
    "The least time, in milliseconds, between the end of one attempt and the start of the next. Default 180000. "
    + "Minimum 0.";

  public void Apply(OpenApiSchema schema, SchemaFilterContext context) {
    ArgumentNullException.ThrowIfNull(schema);
    ArgumentNullException.ThrowIfNull(context);
    if (schema.Properties is null) return;

    if (context.Type == typeof(QueueDeviceRecoveryDto)) {
      schema.Description = FieldDescription;
      Describe(schema, "action", ActionDescription,
        QueueDeviceRecovery.ActionNone, QueueDeviceRecovery.ActionRebootInstance);
      Describe(schema, "afterMs", AfterMsDescription);
      Describe(schema, "maxAttempts", MaxAttemptsDescription);
      Describe(schema, "cooldownMs", CooldownMsDescription);
    }
    else if (context.Type == typeof(CreateQueueRequest)
      || context.Type == typeof(UpdateQueueRequest)
      || context.Type == typeof(QueueResponse)
      || context.Type == typeof(QueueDetailResponse)) {
      Describe(schema, "deviceRecovery", FieldDescription);
    }
  }

  private static void Describe(OpenApiSchema schema, string property, string description, params string[] values) {
    if (!schema.Properties.TryGetValue(property, out var propertySchema)) return;
    propertySchema.Description = description;
    if (values.Length == 0) return;
    propertySchema.Enum = values.Select(v => (IOpenApiAny)new OpenApiString(v)).ToList();
  }
}
