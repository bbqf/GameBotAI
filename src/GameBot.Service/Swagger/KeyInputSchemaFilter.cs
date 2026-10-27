using GameBot.Service.Models;
using Microsoft.OpenApi.Any;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace GameBot.Service.Swagger;

/// <summary>
/// Feature 164: describes the key text of a key input step. The key text can be a key name, a digit, a number pad
/// name, or a raw Android key code. The service does not feed XML comments to Swagger, so this filter sets the text.
/// <see cref="RuleSummary"/> is shared with the <c>key</c> payload text of a primitive action and with the key action
/// definition in the examples, so the rule exists once.
/// </summary>
internal sealed class KeyInputSchemaFilter : ISchemaFilter {
  /// <summary>The rule in one short text, without quotation marks.</summary>
  internal const string RuleSummary =
    "A key name, a digit 0 to 9, a number pad name NUMPAD_0 to NUMPAD_9, or a raw Android key code from 0 to 300. "
    + "A name has priority over a raw key code.";

  internal const string KeyDescription =
    "The key to press. " + RuleSummary + " The names are HOME, BACK, ESCAPE, MENU, ENTER, SPACE, TAB, DEL, DELETE, "
    + "UP, DOWN, LEFT, RIGHT, VOLUME_UP, VOLUME_DOWN, POWER, and A to Z. The service ignores case and ignores spaces "
    + "at the start and end. Example with a digit: \"6\" presses the digit key 6 (key code 13). Example with a raw "
    + "key code: \"85\" presses key code 85 (media play and pause). A key that the service cannot send gives the step "
    + "status not_accepted. The reason is key_input_unknown_key (the text is not a name and not a whole number) or "
    + "key_input_code_out_of_range (a whole number above 300).";

  public void Apply(OpenApiSchema schema, SchemaFilterContext context) {
    if (context.Type != typeof(KeyInputConfigDto)) {
      return;
    }

    if (schema.Properties is not null && schema.Properties.TryGetValue("key", out var key)) {
      key.Description = KeyDescription;
      key.Example = new OpenApiString("6");
    }
  }
}
