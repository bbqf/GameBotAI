using System;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace GameBot.ContractTests.Sequences;

/// <summary>
/// Feature 114 (FR-014): the OpenAPI document tells the image keys of <c>fieldTemplates</c>, the
/// placeholder in <c>imageVisible.imageId</c>, and the template save check.
/// </summary>
public sealed class ParametrizedReferenceImageOpenApiTests {
  private static WebApplicationFactory<Program> CreateFactory() {
    Environment.SetEnvironmentVariable("GAMEBOT_USE_ADB", "false");
    Environment.SetEnvironmentVariable("GAMEBOT_DYNAMIC_PORT", "true");
    Environment.SetEnvironmentVariable("GAMEBOT_AUTH_TOKEN", "test-token");
    return new WebApplicationFactory<Program>();
  }

  private static async Task<JsonElement> ReadSchemasAsync() {
    using var app = CreateFactory();
    var client = app.CreateClient();
    var response = await client.GetAsync(new Uri("/swagger/v1/swagger.json", UriKind.Relative)).ConfigureAwait(false);
    response.EnsureSuccessStatusCode();
    using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync().ConfigureAwait(false));
    return document.RootElement.GetProperty("components").GetProperty("schemas").Clone();
  }

  private static string Description(JsonElement schemas, string schema, string property) =>
      schemas.GetProperty(schema).GetProperty("properties").GetProperty(property)
        .GetProperty("description").GetString() ?? string.Empty;

  [Fact]
  public async Task FieldTemplatesDescribesTheImageKeysAndTheRules() {
    var schemas = await ReadSchemasAsync().ConfigureAwait(false);

    var description = Description(schemas, "CommandStepDto", "fieldTemplates");

    description.Should().Contain("primitiveTap.detectionTarget.referenceImageId")
        .And.Contain("waitForImage.detectionTarget.referenceImageId")
        .And.Contain("one whole placeholder")
        .And.Contain("unknown_field_template_path")
        .And.Contain("invalid_field_template_value");
  }

  [Theory]
  [InlineData("ImageVisibleConditionContract")]
  [InlineData("ImageVisibleCondition")]
  public async Task ImageIdTellsThePlaceholderAndTheSkippedCheck(string schema) {
    var schemas = await ReadSchemasAsync().ConfigureAwait(false);

    Description(schemas, schema, "imageId").Should().Contain("{{name}}").And.Contain("static_check_skipped");
  }

  [Fact]
  public async Task TemplateEntryParameterValuesTellsTheImageCheck() {
    var schemas = await ReadSchemasAsync().ConfigureAwait(false);

    Description(schemas, "TemplateEntrySaveRequest", "parameterValues").Should().Contain("unknown_image_reference");
  }
}
