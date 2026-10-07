#pragma warning disable CA2007, CA1416, CA2000
using System;
using System.Net;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using Xunit;

namespace GameBot.ContractTests.Ocr;

/// <summary>Feature 128: <c>swagger.json</c> shows the OCR read path, the schemas, and the descriptions.</summary>
public sealed class OcrReadOpenApiTests {
  private static async Task<JsonElement> LoadSwaggerAsync(OcrReadTestHost host) {
    var resp = await host.Client.GetAsync(new Uri("/swagger/v1/swagger.json", UriKind.Relative));
    resp.StatusCode.Should().Be(HttpStatusCode.OK);
    return await OcrReadTestHost.ReadJsonAsync(resp);
  }

  [Fact]
  public async Task PathAndOperationAreDocumented() {
    using var host = new OcrReadTestHost();
    var doc = await LoadSwaggerAsync(host);

    var op = doc.GetProperty("paths").GetProperty("/api/ocr/read").GetProperty("post");
    op.GetProperty("operationId").GetString().Should().Be("ReadOcrRegion");
    op.GetProperty("summary").GetString().Should().NotBeNullOrWhiteSpace();
    op.GetProperty("description").GetString().Should().Contain("No input is sent to the emulator");
  }

  [Fact]
  public async Task SchemasHaveDescriptions() {
    using var host = new OcrReadTestHost();
    var doc = await LoadSwaggerAsync(host);
    var schemas = doc.GetProperty("components").GetProperty("schemas");

    foreach (var name in new[] { "OcrReadRequest", "OcrRegionDto", "OcrReadResponse", "OcrParsedValue", "OcrErrorResponse" }) {
      schemas.TryGetProperty(name, out var schema).Should().BeTrue($"schema {name} must exist");
      foreach (var property in schema.GetProperty("properties").EnumerateObject()) {
        property.Value.TryGetProperty("description", out var description).Should().BeTrue($"{name}.{property.Name} needs a description");
        description.GetString().Should().NotBeNullOrWhiteSpace();
      }
    }
  }
}
