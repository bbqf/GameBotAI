using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace GameBot.Service.Endpoints;

internal static class ActionTypesEndpoints {
  private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

  public static IEndpointRouteBuilder MapActionTypeEndpoints(this IEndpointRouteBuilder app, string storageRoot) {
    app.MapGet("/api/action-types", async (HttpContext ctx) => {
      var (catalog, etag) = await LoadCatalog(storageRoot).ConfigureAwait(false);

      var ifNoneMatch = ctx.Request.Headers.IfNoneMatch.ToString();
      if (!string.IsNullOrEmpty(ifNoneMatch) && string.Equals(ifNoneMatch, etag, StringComparison.Ordinal)) {
        return Results.StatusCode(StatusCodes.Status304NotModified);
      }

      ctx.Response.Headers.ETag = etag;
      return Results.Ok(catalog);
    })
    .WithName("ListActionTypes")
    .WithTags("Actions");

    return app;
  }

  private static async Task<(ActionTypeCatalogDto Catalog, string Etag)> LoadCatalog(string storageRoot) {
    var path = Path.Combine(storageRoot, "actions", "action-types.json");
    ActionTypeCatalogDto? catalog = null;
    if (File.Exists(path)) {
      using var stream = File.OpenRead(path);
      catalog = await JsonSerializer.DeserializeAsync<ActionTypeCatalogDto>(stream, JsonOptions).ConfigureAwait(false);
    }

    catalog ??= BuildDefaultCatalog();
    var etag = ComputeEtag(catalog);
    return (catalog, etag);
  }

  private static string ComputeEtag(ActionTypeCatalogDto catalog) {
    var json = JsonSerializer.Serialize(catalog, JsonOptions);
    var hash = SHA256.HashData(Encoding.UTF8.GetBytes(json));
    return $"W/\"{Convert.ToHexString(hash)}\"";
  }

  private static ActionTypeCatalogDto BuildDefaultCatalog() => new() {
    Version = "v1",
    Items = new List<ActionTypeDto> {
      new() {
        Key = "tap",
        DisplayName = "Tap",
        Description = "Tap at coordinates",
        AttributeDefinitions = new List<AttributeDefinitionDto> {
          new() {
            Key = "x",
            Label = "X",
            DataType = "number",
            Required = true,
            Constraints = new AttributeConstraintsDto { Min = 0, Max = 5000 },
            HelpText = "X coordinate"
          },
          new() {
            Key = "y",
            Label = "Y",
            DataType = "number",
            Required = true,
            Constraints = new AttributeConstraintsDto { Min = 0, Max = 5000 },
            HelpText = "Y coordinate"
          },
          new() {
            Key = "mode",
            Label = "Mode",
            DataType = "enum",
            Required = true,
            Constraints = new AttributeConstraintsDto { AllowedValues = new List<string> { "fast", "slow" }, DefaultValue = "fast" },
            HelpText = "Tap mode"
          }
        }
      }
    }
  };
}

internal sealed class ActionTypeCatalogDto {
  public string? Version { get; set; }
  public List<ActionTypeDto> Items { get; set; } = new();
}

internal sealed class ActionTypeDto {
  public required string Key { get; set; }
  public required string DisplayName { get; set; }
  public string? Description { get; set; }
  public string? Version { get; set; }
  public List<AttributeDefinitionDto> AttributeDefinitions { get; set; } = new();
}

internal sealed class AttributeDefinitionDto {
  public required string Key { get; set; }
  public required string Label { get; set; }
  public required string DataType { get; set; }
  public bool Required { get; set; }
  public AttributeConstraintsDto? Constraints { get; set; }
  public string? HelpText { get; set; }
}

internal sealed class AttributeConstraintsDto {
  public double? Min { get; set; }
  public double? Max { get; set; }
  public string? Pattern { get; set; }
  public List<string>? AllowedValues { get; set; }
  public object? DefaultValue { get; set; }
}
